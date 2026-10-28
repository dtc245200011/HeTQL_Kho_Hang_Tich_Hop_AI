using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class ReplenishmentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IAiService _aiService;

        public ReplenishmentController(ApplicationDbContext db, IAiService? aiService = null)
        {
            _db = db;
            _aiService = aiService ?? new AiService(db);
        }

        public async Task<IActionResult> Index()
        {
            var suggestions = await _db.AiSuggestions
                .Where(s => s.Status == "PENDING_REVIEW")
                .OrderByDescending(s => s.CreatedAt)
                .ToListAsync();

            var viewModels = new List<dynamic>();
            foreach (var s in suggestions)
            {
                string skuId = s.SkuId ?? "";
                string productName = "N/A";
                int currentStock = 0;
                int minStock = 0;
                int suggestedQty = 0;
                string reason = s.SuggestionType;

                if (!string.IsNullOrWhiteSpace(s.PayloadJson))
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(s.PayloadJson);
                        var root = doc.RootElement;

                        if (string.IsNullOrEmpty(skuId))
                        {
                            if (root.TryGetProperty("sku", out var skuProp)) skuId = skuProp.GetString() ?? "";
                            else if (root.TryGetProperty("bottleneckSku", out var bProp)) skuId = bProp.GetString() ?? "";
                        }

                        if (root.TryGetProperty("productName", out var pnProp))
                        {
                            productName = pnProp.GetString() ?? "N/A";
                        }

                        if (root.TryGetProperty("currentStock", out var csProp))
                        {
                            currentStock = csProp.GetInt32();
                        }

                        if (root.TryGetProperty("minStock", out var msProp))
                        {
                            minStock = msProp.GetInt32();
                        }
                        else if (root.TryGetProperty("threshold", out var thProp))
                        {
                            minStock = thProp.GetInt32();
                        }

                        if (root.TryGetProperty("suggested", out var sugProp))
                        {
                            suggestedQty = sugProp.GetInt32();
                        }
                        else if (root.TryGetProperty("avg7", out var avg7Prop))
                        {
                            suggestedQty = avg7Prop.GetInt32();
                        }

                        if (root.TryGetProperty("reason", out var reasonProp))
                        {
                            reason = reasonProp.GetString() ?? "";
                        }
                        else if (root.TryGetProperty("diff", out var diffProp))
                        {
                            double diffVal = diffProp.GetDouble();
                            string dir = diffVal >= 0 ? "tăng" : "giảm";
                            reason = $"Xuất bán {dir} bất thường {Math.Round(Math.Abs(diffVal) * 100, 1)}% trong 7 ngày gần nhất.";
                        }

                        // Nếu là cảnh báo sức chứa kho
                        if (s.SuggestionType == "CAPACITY_WARNING" && root.TryGetProperty("warehouse", out var whProp))
                        {
                            productName = $"Kho: {whProp.GetString()}";
                            if (root.TryGetProperty("percent", out var pctProp))
                            {
                                currentStock = (int)Math.Round(pctProp.GetDouble());
                            }
                        }
                    }
                    catch { }
                }

                // Nếu chưa có tên hàng hóa hoặc định mức, tra cứu theo SkuId
                if (!string.IsNullOrEmpty(skuId))
                {
                    var skuVariant = await _db.SkuVariants.FirstOrDefaultAsync(x => x.SkuId == skuId);
                    if (skuVariant != null)
                    {
                        var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == skuVariant.ProductModelId);
                        if (pm != null)
                        {
                            if (productName == "N/A" || string.IsNullOrWhiteSpace(productName))
                                productName = pm.ProductName ?? skuId;
                            if (minStock == 0) minStock = pm.MinStock;
                        }
                    }
                    if (currentStock == 0 && s.SuggestionType != "CAPACITY_WARNING")
                    {
                        currentStock = await _db.StockLedgers.Where(l => l.SkuId == skuId && l.Status == "GOOD").SumAsync(l => l.Quantity);
                    }
                }

                // Chuẩn hóa câu lý do mặc định nếu chưa có
                if (string.IsNullOrWhiteSpace(reason) || reason == s.SuggestionType)
                {
                    reason = s.SuggestionType switch
                    {
                        "LOW_STOCK" => $"Tồn kho khả dụng ({currentStock}) thấp hơn định mức tối thiểu ({minStock}).",
                        "REPLENISHMENT" => $"Dự báo nhu cầu đặt hàng chu kỳ tới dựa trên tốc độ tiêu thụ.",
                        "ANOMALY" => "Tốc độ xuất bán biến động bất thường so với mức trung bình.",
                        "BOTTLENECK" => "Linh kiện nghẽn cổ chai giới hạn khả năng ráp bộ Combo.",
                        "CAPACITY_WARNING" => $"Kho hàng đạt {currentStock}% sức chứa.",
                        _ => s.SuggestionType
                    };
                }

                // Dự phòng số lượng đề xuất cho cảnh báo tồn thấp nếu chưa tính
                if (suggestedQty <= 0 && s.SuggestionType == "LOW_STOCK" && minStock > currentStock)
                {
                    suggestedQty = Math.Max(1, minStock - currentStock);
                }

                viewModels.Add(new
                {
                    Id = s.SuggestionId,
                    Type = s.SuggestionType,
                    Date = s.CreatedAt,
                    Sku = skuId,
                    ProductName = productName,
                    CurrentStock = currentStock,
                    MinStock = minStock,
                    SuggestedQty = suggestedQty,
                    Reason = reason
                });
            }

            return View(viewModels);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunAiScan()
        {
            try
            {
                await _aiService.RunAnalysisAsync();
                TempData["Message"] = "Đã quét phân tích dữ liệu AI thành công và cập nhật các đề xuất mới!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi chạy quét AI: " + ex.Message;
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string id)
        {
            var suggestion = await _db.AiSuggestions.FirstOrDefaultAsync(s => s.SuggestionId == id);
            if (suggestion == null) return NotFound();

            suggestion.Status = "APPROVED";
            suggestion.ReviewedBy = User?.Identity?.Name ?? "Quản lý kho";
            suggestion.ReviewedAt = DateTime.UtcNow;

            int suggestedQty = 0;
            string skuId = suggestion.SkuId ?? "";
            string payloadProductName = "";
            string payloadReason = "";
            string sourceWarehouse = "";

            if (!string.IsNullOrWhiteSpace(suggestion.PayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(suggestion.PayloadJson);
                    var root = doc.RootElement;
                    if (string.IsNullOrEmpty(skuId))
                    {
                        if (root.TryGetProperty("sku", out var skuProp)) skuId = skuProp.GetString() ?? "";
                        else if (root.TryGetProperty("bottleneckSku", out var bProp)) skuId = bProp.GetString() ?? "";
                    }
                    if (root.TryGetProperty("productName", out var pnProp)) payloadProductName = pnProp.GetString() ?? "";
                    if (root.TryGetProperty("reason", out var rProp)) payloadReason = rProp.GetString() ?? "";
                    if (root.TryGetProperty("warehouse", out var whProp)) sourceWarehouse = whProp.GetString() ?? "";

                    if (root.TryGetProperty("suggested", out var sugProp)) suggestedQty = sugProp.GetInt32();
                    else if (root.TryGetProperty("avg7", out var avg7Prop)) suggestedQty = avg7Prop.GetInt32();

                    // Fallback tính số lượng nếu là LOW_STOCK
                    if (suggestedQty <= 0 && suggestion.SuggestionType == "LOW_STOCK")
                    {
                        int current = root.TryGetProperty("currentStock", out var cProp) ? cProp.GetInt32() : 0;
                        int min = root.TryGetProperty("minStock", out var mProp) ? mProp.GetInt32() : 10;
                        suggestedQty = Math.Max(1, min - current);
                    }
                }
                catch { }
            }

            // Tra cứu thông tin chi tiết sản phẩm
            var skuVariant = !string.IsNullOrEmpty(skuId) ? await _db.SkuVariants.FirstOrDefaultAsync(x => x.SkuId == skuId) : null;
            var pm = skuVariant != null ? await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == skuVariant.ProductModelId) : null;
            if (pm == null && !string.IsNullOrEmpty(skuId))
            {
                pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == skuId);
            }

            string productName = !string.IsNullOrWhiteSpace(pm?.ProductName)
                ? pm.ProductName
                : (!string.IsNullOrWhiteSpace(payloadProductName) && payloadProductName != "N/A" ? payloadProductName : skuId);
            string unit = !string.IsNullOrWhiteSpace(pm?.Unit) ? pm.Unit : "Cái";
            decimal unitPrice = skuVariant?.UnitPrice > 0 ? skuVariant.UnitPrice : 0m;
            if (unitPrice == 0 && !string.IsNullOrEmpty(skuId))
            {
                var prevLine = await _db.VoucherLines.Where(l => l.SkuId == skuId && l.UnitPrice > 0).OrderByDescending(l => l.Id).FirstOrDefaultAsync();
                if (prevLine != null) unitPrice = prevLine.UnitPrice;
            }

            // Ưu tiên chọn Kho Tổng (MAIN) đang hoạt động, không gán nhầm vào kho hàng lỗi (WH-DAMAGED)
            var warehouse = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseType == "MAIN" && w.IsActive)
                ?? await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseType != "DAMAGED" && w.IsActive)
                ?? await _db.Warehouses.FirstOrDefaultAsync();
            string targetWarehouseId = warehouse?.WarehouseId ?? "WH-GLOBAL";

            // Chọn Nhà cung cấp mặc định đang hoạt động
            var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.IsActive) ?? await _db.Suppliers.FirstOrDefaultAsync();
            string counterparty = supplier?.Name ?? "NCC Nội Thất 1";
            string address = supplier?.Address ?? "Kho hàng nhà cung cấp";

            string typeDesc = suggestion.SuggestionType switch
            {
                "LOW_STOCK" => "Tồn kho thấp dưới định mức",
                "REPLENISHMENT" => "Dự báo nhu cầu đặt hàng",
                "BOTTLENECK" => "Cấu kiện nghẽn cổ chai Combo",
                "ANOMALY" => "Biến động xuất bán bất thường",
                "CAPACITY_WARNING" => "Cảnh báo sức chứa kho",
                _ => suggestion.SuggestionType
            };

            string fullReason = string.IsNullOrWhiteSpace(payloadReason)
                ? "Đề xuất tự động từ AI"
                : $"Đề xuất tự động từ AI ({typeDesc}): {payloadReason}";

            InventoryVoucher? voucher = null;
            if (!string.IsNullOrWhiteSpace(skuId) && suggestedQty > 0)
            {
                decimal lineTotal = unitPrice * suggestedQty;
                var voucherId = Guid.NewGuid().ToString();

                voucher = new InventoryVoucher
                {
                    VoucherId = voucherId,
                    VoucherNumber = "PN-AI-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    Date = DateTime.UtcNow,
                    Type = "INBOUND",
                    Status = "DRAFT",
                    Counterparty = counterparty,
                    Address = address,
                    WarehouseId = targetWarehouseId,
                    Reason = fullReason,
                    ReferenceDocument = "AI-REPLENISHMENT-" + DateTime.UtcNow.ToString("yyyyMMdd"),
                    CreatedBy = "Trợ lý AI" + (string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "" : $" ({User.Identity.Name})"),
                    TotalAmount = lineTotal,
                    Lines = new List<VoucherLine> {
                        new VoucherLine {
                            VoucherId = voucherId,
                            SkuId = skuId,
                            SkuCode = skuId,
                            ProductName = productName,
                            Unit = unit,
                            Quantity = suggestedQty,
                            DocumentQuantity = suggestedQty,
                            BatchNumber = "LÔ-AI-" + DateTime.UtcNow.ToString("yyyyMMdd"),
                            ExpiryDate = DateTime.UtcNow.AddYears(2),
                            UnitPrice = unitPrice,
                            LineTotal = lineTotal
                        }
                    }
                };
                _db.InventoryVouchers.Add(voucher);
                TempData["Message"] = $"Đã duyệt đề xuất AI ({typeDesc}) và tự động tạo Phiếu Nhập Kho nháp ({voucher.VoucherNumber}) cho sản phẩm '{productName}' ({skuId}) với số lượng {suggestedQty} {unit}, tổng tiền: {lineTotal:N0} đ.";
                TempData["CreatedVoucherId"] = voucher.VoucherId;
                TempData["CreatedVoucherNumber"] = voucher.VoucherNumber;
                TempData["CreatedVoucherType"] = "INBOUND";
            }
            else if (suggestion.SuggestionType == "CAPACITY_WARNING" && !string.IsNullOrWhiteSpace(sourceWarehouse))
            {
                // Cảnh báo sức chứa: Tự động sinh phiếu điều chuyển (TRANSFER) nháp để giảm tải kho
                var targetWh = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId != sourceWarehouse && w.WarehouseType != "DAMAGED" && w.IsActive)
                    ?? await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId != sourceWarehouse && w.IsActive);

                var topLedger = await _db.StockLedgers
                    .Where(l => l.WarehouseId == sourceWarehouse && l.Status == "GOOD" && l.Quantity > 0)
                    .OrderByDescending(l => l.Quantity)
                    .FirstOrDefaultAsync();

                if (topLedger != null && targetWh != null)
                {
                    int transferQty = Math.Max(1, (int)Math.Ceiling(topLedger.Quantity * 0.2));
                    var skuVar = await _db.SkuVariants.FirstOrDefaultAsync(s => s.SkuId == topLedger.SkuId);
                    var pmVar = skuVar != null ? await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == skuVar.ProductModelId) : null;
                    var prodName = pmVar?.ProductName ?? topLedger.SkuId;
                    var uPrice = skuVar?.UnitPrice ?? 0m;
                    var lTotal = uPrice * transferQty;
                    var vId = Guid.NewGuid().ToString();

                    voucher = new InventoryVoucher
                    {
                        VoucherId = vId,
                        VoucherNumber = "CK-AI-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                        Date = DateTime.UtcNow,
                        Type = "TRANSFER",
                        Status = "DRAFT",
                        Counterparty = "Điều chuyển nội bộ giảm tải",
                        WarehouseId = sourceWarehouse,
                        ToWarehouseId = targetWh.WarehouseId,
                        Reason = fullReason,
                        ReferenceDocument = "AI-TRANSFER-CAPACITY-" + DateTime.UtcNow.ToString("yyyyMMdd"),
                        CreatedBy = "Trợ lý AI" + (string.IsNullOrWhiteSpace(User?.Identity?.Name) ? "" : $" ({User.Identity.Name})"),
                        TotalAmount = lTotal,
                        Lines = new List<VoucherLine> {
                            new VoucherLine {
                                VoucherId = vId,
                                SkuId = topLedger.SkuId,
                                SkuCode = topLedger.SkuId,
                                ProductName = prodName,
                                Unit = pmVar?.Unit ?? "Cái",
                                Quantity = transferQty,
                                DocumentQuantity = transferQty,
                                BatchNumber = "LÔ-AI-" + DateTime.UtcNow.ToString("yyyyMMdd"),
                                UnitPrice = uPrice,
                                LineTotal = lTotal
                            }
                        }
                    };
                    _db.InventoryVouchers.Add(voucher);
                    TempData["Message"] = $"Đã duyệt đề xuất AI ({typeDesc}) và tự động tạo Phiếu Chuyển Kho nháp ({voucher.VoucherNumber}) điều chuyển {transferQty} {pmVar?.Unit ?? "Cái"} '{prodName}' từ kho {sourceWarehouse} sang kho {targetWh.WarehouseName ?? targetWh.WarehouseId}.";
                    TempData["CreatedVoucherId"] = voucher.VoucherId;
                    TempData["CreatedVoucherNumber"] = voucher.VoucherNumber;
                    TempData["CreatedVoucherType"] = "TRANSFER";
                }
                else
                {
                    TempData["Message"] = $"Đã phê duyệt đề xuất từ AI ({typeDesc}).";
                }
            }
            else
            {
                TempData["Message"] = $"Đã phê duyệt đề xuất từ AI ({typeDesc}).";
            }

            // Ghi nhận AuditLog tuân thủ SRS-6.5
            var audit = new AuditLog
            {
                EntityType = "AI_SUGGESTION",
                EntityId = suggestion.SuggestionId,
                Action = "APPROVE_AI_SUGGESTION",
                BeforeJson = suggestion.PayloadJson,
                AfterJson = voucher != null
                    ? JsonSerializer.Serialize(new
                    {
                        voucherNumber = voucher.VoucherNumber,
                        type = voucher.Type,
                        skuId = skuId,
                        productName = productName,
                        quantity = suggestedQty,
                        counterparty = voucher.Counterparty,
                        warehouseId = voucher.WarehouseId,
                        totalAmount = voucher.TotalAmount
                    })
                    : "Status: APPROVED",
                PerformedBy = User?.Identity?.Name ?? "Quản lý kho",
                PerformedAt = DateTime.UtcNow,
                Reason = $"Duyệt đề xuất AI ({suggestion.SuggestionType}) - Tạo phiếu {voucher?.VoucherNumber ?? "N/A"}"
            };
            _db.AuditLogs.Add(audit);

            await _db.SaveChangesAsync();
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(string id)
        {
            var suggestion = await _db.AiSuggestions.FirstOrDefaultAsync(s => s.SuggestionId == id);
            if (suggestion == null) return NotFound();

            suggestion.Status = "REJECTED";
            suggestion.ReviewedBy = User?.Identity?.Name ?? "Quản lý kho";
            suggestion.ReviewedAt = DateTime.UtcNow;

            // Ghi nhận AuditLog tuân thủ SRS-6.5
            var audit = new AuditLog
            {
                EntityType = "AI_SUGGESTION",
                EntityId = suggestion.SuggestionId,
                Action = "REJECT_AI_SUGGESTION",
                BeforeJson = suggestion.PayloadJson,
                AfterJson = "Status: REJECTED",
                PerformedBy = User?.Identity?.Name ?? "Quản lý kho",
                PerformedAt = DateTime.UtcNow,
                Reason = $"Từ chối đề xuất AI ({suggestion.SuggestionType})"
            };
            _db.AuditLogs.Add(audit);

            await _db.SaveChangesAsync();

            TempData["Message"] = "Đã từ chối đề xuất từ AI.";
            return RedirectToAction("Index");
        }
    }
}
