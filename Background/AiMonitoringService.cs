using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Background
{
    public class AiMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<AiMonitoringService> _logger;

        public AiMonitoringService(IServiceProvider sp, ILogger<AiMonitoringService> logger)
        {
            _sp = sp; _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();
                    var emailSender = scope.ServiceProvider.GetRequiredService<IEmailSender>();

                    var config = await db.SystemConfigs.FirstOrDefaultAsync();
                    int lowStockThreshold = config?.LowStockAlertThreshold ?? 10;
                    int capacityAlertPercent = config?.CapacityAlertPercent ?? 90;

                    // Fetch data and detect anomalies
                    var since60 = DateTime.UtcNow.AddDays(-60);
                    var since7 = DateTime.UtcNow.AddDays(-7);

                    var movements60 = await db.StockMovements.Where(m => m.CreatedAt >= since60).ToListAsync();
                    var movements7 = await db.StockMovements.Where(m => m.CreatedAt >= since7).ToListAsync();

                    // Simple anomaly detection per SKU by comparing averages
                    var skuIds = movements60.Select(m => m.SkuId).Union(movements7.Select(m => m.SkuId)).Distinct();
                    foreach(var sku in skuIds)
                    {
                        var avg60 = movements60.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        var avg7 = movements7.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        if (avg60 > 0)
                        {
                            var diff = Math.Abs(avg7 - avg60) / (double)avg60;
                            if (diff >= 0.2)
                            {
                                var payload = new { sku = sku, avg60 = avg60, avg7 = avg7, diff = diff };
                                db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "ANOMALY", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                            }
                        }
                    }

                    // Low Stock Alert (Based on SystemConfig)
                    var allLedgers = await db.StockLedgers.Where(l => l.Status == "GOOD").ToListAsync();
                    var stockBySku = allLedgers.GroupBy(l => l.SkuId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
                    
                    var allSkus = await db.SkuVariants.Select(s => s.SkuId).ToListAsync();
                    foreach (var skuCode in allSkus)
                    {
                        var totalStock = stockBySku.ContainsKey(skuCode) ? stockBySku[skuCode] : 0;
                        if (totalStock < lowStockThreshold)
                        {
                            var payload = new { sku = skuCode, currentStock = totalStock, threshold = lowStockThreshold };
                            // Check if an unreviewed suggestion already exists to prevent spamming
                            var exists = await db.AiSuggestions.AnyAsync(a => a.SkuId == skuCode && a.SuggestionType == "LOW_STOCK" && a.Status == "PENDING_REVIEW");
                            if (!exists)
                            {
                                db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "LOW_STOCK", SkuId = skuCode, PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                                
                                if (config?.EnableEmailAlerts == true && !string.IsNullOrWhiteSpace(config.Email))
                                {
                                    await emailSender.SendEmailAsync(config.Email, 
                                        $"[CẢNH BÁO TỒN KHO] SKU: {skuCode}", 
                                        $"<p>Hệ thống ghi nhận sản phẩm <b>{skuCode}</b> đang có mức tồn kho là {totalStock}, thấp hơn định mức tối thiểu ({lowStockThreshold}).</p><p>Vui lòng kiểm tra và lên kế hoạch nhập hàng.</p>");
                                }
                            }
                        }
                    }

                    // Warehouse Capacity Alert (Based on SystemConfig)
                    var warehouses = await db.Warehouses.ToListAsync();
                    foreach(var wh in warehouses)
                    {
                        var whStock = allLedgers.Where(l => l.WarehouseId == wh.WarehouseId).Sum(l => l.Quantity);
                        // Convert quantity to CBM if needed, here we assume 1 qty = roughly 0.1 CBM for simplicity, or just use qty.
                        decimal currentCbm = (decimal)whStock * 0.1m; 
                        if (wh.MaxCapacityCbm > 0)
                        {
                            var pct = (currentCbm / wh.MaxCapacityCbm) * 100;
                            if (pct >= capacityAlertPercent)
                            {
                                var payload = new { warehouse = wh.WarehouseId, percent = pct, threshold = capacityAlertPercent };
                                // Check if unreviewed alert exists
                                var existsCapacity = await db.AiSuggestions.AnyAsync(a => a.SuggestionType == "CAPACITY_WARNING" && a.Status == "PENDING_REVIEW" && a.PayloadJson.Contains(wh.WarehouseId));
                                if (!existsCapacity)
                                {
                                    db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "CAPACITY_WARNING", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                                    
                                    if (config?.EnableEmailAlerts == true && !string.IsNullOrWhiteSpace(config.Email))
                                    {
                                        await emailSender.SendEmailAsync(config.Email, 
                                            $"[CẢNH BÁO SỨC CHỨA] Kho: {wh.WarehouseId}", 
                                            $"<p>Kho <b>{wh.WarehouseId}</b> đã đạt sức chứa {Math.Round(pct, 2)}%, vượt qua mức cảnh báo ({capacityAlertPercent}%).</p><p>Vui lòng kiểm tra và điều chuyển hàng hóa.</p>");
                                    }
                                }
                            }
                        }
                    }

                    // Bottleneck detection for combos
                    var combos = await db.ComboProducts.Include(c => c.Components).ToListAsync();
                    foreach(var combo in combos)
                    {
                        // compute available sets
                        int minSets = int.MaxValue; string bottleneckSku = string.Empty;
                        foreach(var comp in combo.Components)
                        {
                            var total = await db.StockLedgers.Where(l => l.SkuId == comp.SkuId && l.Status == "GOOD").SumAsync(l => l.Quantity);
                            var sets = total / Math.Max(1, comp.QuantityPerSet);
                            if (sets < minSets) { minSets = sets; bottleneckSku = comp.SkuId; }
                        }
                        if (minSets < combo.MinStock)
                        {
                            var payload = new { combo = combo.ComboId, minSets = minSets, bottleneckSku = bottleneckSku };
                            db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "BOTTLENECK", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                        }
                    }

                    // Replenishment suggestion: use simple formula per SKU
                    foreach(var sku in skuIds)
                    {
                        var avg60 = movements60.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        double seasonal = 1.0; // simple placeholder, could be improved
                        var suggested = (int)Math.Ceiling(avg60 * seasonal - allLedgers.Where(l => l.SkuId == sku).Sum(l => l.Quantity));
                        if (suggested > 0)
                        {
                            var payload = new { sku = sku, suggested = suggested };
                            db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "REPLENISHMENT", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                        }
                    }

                    await db.SaveChangesAsync();
                    var currentIntervalMinutes = config?.AiScanIntervalMinutes > 0 ? config.AiScanIntervalMinutes : 30;

                    await Task.Delay(TimeSpan.FromMinutes(currentIntervalMinutes), stoppingToken);
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Error in AiMonitoringService");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); // delay 1 min on error before retry
                }
            }
        }
    }
}
