using System.Text.Json;
using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace DuAnCode.Web.Services
{
    public class AiService : IAiService
    {
        private readonly ApplicationDbContext _db;
        private readonly IEmailSender? _emailSender;
        private readonly ILogger<AiService>? _logger;

        public AiService(ApplicationDbContext db, IEmailSender? emailSender = null, ILogger<AiService>? logger = null)
        {
            _db = db;
            _emailSender = emailSender;
            _logger = logger;
        }

        public async Task<string> SuggestReplenishmentAsync(string skuId)
        {
            var sku = await _db.SkuVariants.FirstOrDefaultAsync(s => s.SkuId == skuId);
            if (sku == null) return "SKU not found";

            var total = await _db.StockLedgers.Where(x => x.SkuId == skuId && x.Status == "GOOD").SumAsync(x => (int?)x.Quantity) ?? 0;
            var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == sku.ProductModelId);
            var min = pm?.MinStock ?? 0;
            if (total < min)
            {
                var need = min - total;
                return $"Suggest reorder {need} units for {skuId} to reach min stock {min}.";
            }
            return "Stock level adequate.";
        }

        public async Task<IEnumerable<dynamic>> GetAllSkusAsync()
        {
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            return skus.Select(s => (dynamic)s).ToList();
        }

        public async Task<IEnumerable<dynamic>> GetAllLedgersAsync()
        {
            var ledgers = await _db.StockLedgers.AsNoTracking().ToListAsync();
            return ledgers.Select(l => (dynamic)l).ToList();
        }

        public async Task RunAnalysisAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var config = await _db.SystemConfigs.FirstOrDefaultAsync(cancellationToken);
                int lowStockThreshold = config?.LowStockAlertThreshold > 0 ? config.LowStockAlertThreshold : 10;
                int capacityAlertPercent = config?.CapacityAlertPercent > 0 ? config.CapacityAlertPercent : 90;

                var productModels = await _db.ProductModels.ToDictionaryAsync(p => p.ProductModelId, cancellationToken);
                var skuVariants = await _db.SkuVariants.ToListAsync(cancellationToken);
                var allLedgers = await _db.StockLedgers.Where(l => l.Status == "GOOD").ToListAsync(cancellationToken);
                var stockBySku = allLedgers.GroupBy(l => l.SkuId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));

                var since60 = DateTime.UtcNow.AddDays(-60);
                var since7 = DateTime.UtcNow.AddDays(-7);

                // SRS-AI-01 & SRS-AI-02: Chi tinh tren so luong xuat cua hang trang thai GOOD
                var movements60 = await _db.StockMovements
                    .Where(m => m.CreatedAt >= since60 && m.MovementType == "OUTBOUND" && m.StockStatus == "GOOD")
                    .ToListAsync(cancellationToken);
                var movements7 = movements60.Where(m => m.CreatedAt >= since7).ToList();

                // 1. Canh bao ton kho thap (LOW_STOCK) dua tren MinStock tung san pham
                foreach (var sku in skuVariants)
                {
                    var totalStock = stockBySku.ContainsKey(sku.SkuId) ? stockBySku[sku.SkuId] : 0;
                    var pm = productModels.ContainsKey(sku.ProductModelId) ? productModels[sku.ProductModelId] : null;
                    var minStock = pm != null && pm.MinStock > 0 ? pm.MinStock : lowStockThreshold;

                    if (totalStock < minStock)
                    {
                        var neededQty = minStock - totalStock;
                        var suggested = Math.Max(neededQty, minStock);

                        var exists = await _db.AiSuggestions.AnyAsync(a => a.SkuId == sku.SkuId && a.SuggestionType == "LOW_STOCK" && a.Status == "PENDING_REVIEW", cancellationToken);
                        if (!exists)
                        {
                            var payload = new
                            {
                                sku = sku.SkuId,
                                productName = pm?.ProductName ?? sku.SkuId,
                                currentStock = totalStock,
                                minStock = minStock,
                                suggested = suggested,
                                threshold = minStock,
                                reason = $"Tồn kho khả dụng ({totalStock}) thấp hơn định mức tối thiểu ({minStock})."
                            };
                            _db.AiSuggestions.Add(new AiSuggestion
                            {
                                SuggestionType = "LOW_STOCK",
                                SkuId = sku.SkuId,
                                PayloadJson = JsonSerializer.Serialize(payload)
                            });

                            if (config?.EnableEmailAlerts == true && !string.IsNullOrWhiteSpace(config.Email) && _emailSender != null)
                            {
                                await _emailSender.SendEmailAsync(config.Email,
                                    $"[CẢNH BÁO TỒN KHO] SKU: {sku.SkuId}",
                                    $"<p>Sản phẩm <b>{pm?.ProductName ?? sku.SkuId}</b> (Mã: {sku.SkuId}) đang có mức tồn kho là {totalStock}, thấp hơn định mức an toàn ({minStock}).</p><p>Đề xuất nhập bù: <b>{suggested}</b> đơn vị.</p>");
                            }
                        }
                    }
                }

                // 2. SRS-AI-02: Phat hien bien dong bat thuong (Anomaly Detection >= 20%)
                var skuIdsWithMovements = movements60.Select(m => m.SkuId).Distinct();
                foreach (var skuId in skuIdsWithMovements)
                {
                    var sum60 = movements60.Where(m => m.SkuId == skuId).Sum(m => Math.Abs(m.QuantityDelta));
                    var sum7 = movements7.Where(m => m.SkuId == skuId).Sum(m => Math.Abs(m.QuantityDelta));
                    var avgDaily60 = sum60 / 60.0;
                    var avgDaily7 = sum7 / 7.0;

                    if (avgDaily60 > 0)
                    {
                        var diff = (avgDaily7 - avgDaily60) / avgDaily60;
                        if (Math.Abs(diff) >= 0.20)
                        {
                            var exists = await _db.AiSuggestions.AnyAsync(a => a.SkuId == skuId && a.SuggestionType == "ANOMALY" && a.Status == "PENDING_REVIEW", cancellationToken);
                            if (!exists)
                            {
                                string dir = diff >= 0 ? "TĂNG" : "GIẢM";
                                int percent = (int)Math.Round(Math.Abs(diff) * 100);
                                var skuVar = skuVariants.FirstOrDefault(s => s.SkuId == skuId);
                                var pm = skuVar != null && productModels.ContainsKey(skuVar.ProductModelId) ? productModels[skuVar.ProductModelId] : null;

                                int currentStock = stockBySku.ContainsKey(skuId) ? stockBySku[skuId] : 0;
                                int suggestedDemand30 = (int)Math.Ceiling(avgDaily7 * 30);
                                int suggestedOrder = Math.Max(0, suggestedDemand30 - currentStock);
                                int suggested = suggestedOrder > 0 ? suggestedOrder : (int)Math.Ceiling(avgDaily7 * 7);

                                var payload = new
                                {
                                    sku = skuId,
                                    productName = pm?.ProductName ?? skuId,
                                    avg60 = Math.Round(avgDaily60, 2),
                                    avg7 = Math.Round(avgDaily7, 2),
                                    diff = Math.Round(diff, 4),
                                    percent = percent,
                                    direction = dir,
                                    currentStock = currentStock,
                                    suggested = suggested,
                                    reason = $"Nhu cầu xuất bán {dir} bất thường {percent}% trong 7 ngày gần nhất so với trung bình 60 ngày."
                                };
                                _db.AiSuggestions.Add(new AiSuggestion
                                {
                                    SuggestionType = "ANOMALY",
                                    SkuId = skuId,
                                    PayloadJson = JsonSerializer.Serialize(payload)
                                });
                            }
                        }
                    }
                }

                // 3. SRS-AI-03: Canh bao cau kien nghen co chai (Combo Bottleneck Detection)
                var combos = await _db.ComboProducts.Include(c => c.Components).ToListAsync(cancellationToken);
                foreach (var combo in combos)
                {
                    int minSets = int.MaxValue;
                    string bottleneckSku = string.Empty;
                    int bottleneckPerSet = 1;
                    int bottleneckStock = 0;

                    foreach (var comp in combo.Components)
                    {
                        var total = stockBySku.ContainsKey(comp.SkuId) ? stockBySku[comp.SkuId] : 0;
                        var qtyPerSet = Math.Max(1, comp.QuantityPerSet);
                        var sets = total / qtyPerSet;
                        if (sets < minSets)
                        {
                            minSets = sets;
                            bottleneckSku = comp.SkuId;
                            bottleneckPerSet = qtyPerSet;
                            bottleneckStock = total;
                        }
                    }
                    if (minSets == int.MaxValue) minSets = 0;

                    if (minSets < combo.MinStock && !string.IsNullOrEmpty(bottleneckSku))
                    {
                        var exists = await _db.AiSuggestions.AnyAsync(a => a.SkuId == bottleneckSku && a.SuggestionType == "BOTTLENECK" && a.Status == "PENDING_REVIEW", cancellationToken);
                        if (!exists)
                        {
                            var neededSets = combo.MinStock - minSets;
                            var suggestedQty = neededSets * bottleneckPerSet;
                            var skuVar = skuVariants.FirstOrDefault(s => s.SkuId == bottleneckSku);
                            var pm = skuVar != null && productModels.ContainsKey(skuVar.ProductModelId) ? productModels[skuVar.ProductModelId] : null;

                            var payload = new
                            {
                                combo = combo.ComboId,
                                comboName = combo.ComboName,
                                sku = bottleneckSku,
                                productName = pm?.ProductName != null ? $"{pm.ProductName} (Cấu kiện)" : bottleneckSku,
                                minSets = minSets,
                                targetSets = combo.MinStock,
                                bottleneckStock = bottleneckStock,
                                suggested = suggestedQty,
                                reason = $"Linh kiện cổ chai giới hạn combo '{combo.ComboName}'. Hiện chỉ đủ ráp {minSets}/{combo.MinStock} bộ."
                            };
                            _db.AiSuggestions.Add(new AiSuggestion
                            {
                                SuggestionType = "BOTTLENECK",
                                SkuId = bottleneckSku,
                                PayloadJson = JsonSerializer.Serialize(payload)
                            });
                        }
                    }
                }

                // 4. SRS-AI-01: Du bao nhu cau nhap hang (Demand Forecasting)
                int currentMonth = DateTime.UtcNow.Month;
                double seasonal = (currentMonth is 10 or 11 or 12 or 1) ? 1.2 : (currentMonth is 6 or 7) ? 0.8 : 1.0;

                foreach (var skuId in skuIdsWithMovements)
                {
                    var sum60 = movements60.Where(m => m.SkuId == skuId).Sum(m => Math.Abs(m.QuantityDelta));
                    var sum7 = movements7.Where(m => m.SkuId == skuId).Sum(m => Math.Abs(m.QuantityDelta));
                    var avgDaily60 = sum60 / 60.0;
                    var avgDaily7 = sum7 / 7.0;

                    var demand60_30d = avgDaily60 * 30.0;
                    var variance30d = (avgDaily7 - avgDaily60) * 30.0;
                    var forecast = (demand60_30d * seasonal) + variance30d;
                    var currentStock = stockBySku.ContainsKey(skuId) ? stockBySku[skuId] : 0;
                    var suggested = (int)Math.Ceiling(forecast - currentStock);

                    if (suggested > 0)
                    {
                        var exists = await _db.AiSuggestions.AnyAsync(a => a.SkuId == skuId && a.SuggestionType == "REPLENISHMENT" && a.Status == "PENDING_REVIEW", cancellationToken);
                        if (!exists)
                        {
                            var skuVar = skuVariants.FirstOrDefault(s => s.SkuId == skuId);
                            var pm = skuVar != null && productModels.ContainsKey(skuVar.ProductModelId) ? productModels[skuVar.ProductModelId] : null;

                            var payload = new
                            {
                                sku = skuId,
                                productName = pm?.ProductName ?? skuId,
                                avg60 = Math.Round(avgDaily60, 2),
                                avg7 = Math.Round(avgDaily7, 2),
                                seasonal = seasonal,
                                variance = Math.Round(variance30d, 1),
                                currentStock = currentStock,
                                suggested = suggested,
                                reason = $"Dự báo nhu cầu chu kỳ tới ({Math.Round(forecast, 0)} cái, hệ số mùa vụ {seasonal}x) vượt tồn kho khả dụng ({currentStock})."
                            };
                            _db.AiSuggestions.Add(new AiSuggestion
                            {
                                SuggestionType = "REPLENISHMENT",
                                SkuId = skuId,
                                PayloadJson = JsonSerializer.Serialize(payload)
                            });
                        }
                    }
                }

                // 5. Canh bao suc chua kho (CAPACITY_WARNING)
                var warehouses = await _db.Warehouses.ToListAsync(cancellationToken);
                var skuCbmMap = skuVariants.ToDictionary(s => s.SkuId, s => s.Cbm > 0 ? s.Cbm : 0.1m);
                foreach (var wh in warehouses)
                {
                    var whLedgers = allLedgers.Where(l => l.WarehouseId == wh.WarehouseId).ToList();
                    decimal currentCbm = whLedgers.Sum(l => (decimal)l.Quantity * (skuCbmMap.TryGetValue(l.SkuId, out var cbmVal) ? cbmVal : 0.1m));
                    if (wh.MaxCapacityCbm > 0)
                    {
                        var pct = (currentCbm / wh.MaxCapacityCbm) * 100;
                        if (pct >= capacityAlertPercent)
                        {
                            var existsCapacity = await _db.AiSuggestions.AnyAsync(a => a.SuggestionType == "CAPACITY_WARNING" && a.Status == "PENDING_REVIEW" && a.PayloadJson.Contains(wh.WarehouseId), cancellationToken);
                            if (!existsCapacity)
                            {
                                var payload = new
                                {
                                    warehouse = wh.WarehouseId,
                                    warehouseName = wh.WarehouseName,
                                    currentCbm = Math.Round(currentCbm, 2),
                                    maxCbm = wh.MaxCapacityCbm,
                                    percent = Math.Round(pct, 1),
                                    threshold = capacityAlertPercent,
                                    reason = $"Kho {wh.WarehouseName ?? wh.WarehouseId} đã đạt {Math.Round(pct, 1)}% sức chứa (vượt ngưỡng {capacityAlertPercent}%)."
                                };
                                _db.AiSuggestions.Add(new AiSuggestion
                                {
                                    SuggestionType = "CAPACITY_WARNING",
                                    PayloadJson = JsonSerializer.Serialize(payload)
                                });

                                if (config?.EnableEmailAlerts == true && !string.IsNullOrWhiteSpace(config.Email) && _emailSender != null)
                                {
                                    await _emailSender.SendEmailAsync(config.Email,
                                        $"[CẢNH BÁO SỨC CHỨA] Kho: {wh.WarehouseId}",
                                        $"<p>Kho <b>{wh.WarehouseName ?? wh.WarehouseId}</b> đã đạt sức chứa {Math.Round(pct, 1)}%, vượt qua mức cảnh báo ({capacityAlertPercent}%).</p><p>Vui lòng kiểm tra và lên kế hoạch điều chuyển hàng hóa.</p>");
                                }
                            }
                        }
                    }
                }

                await _db.SaveChangesAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Lỗi xảy ra trong quá trình chạy phân tích AI");
                throw;
            }
        }
    }
}
