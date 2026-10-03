using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
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
        public ReplenishmentController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var suggestions = await _db.AiSuggestions.Where(s => s.Status == "PENDING_REVIEW").OrderByDescending(s => s.CreatedAt).ToListAsync();
            
            var viewModels = new List<dynamic>();
            foreach(var s in suggestions)
            {
                string productName = "N/A";
                int currentStock = 0;
                int minStock = 0;
                int suggestedQty = 0;
                string reason = s.SuggestionType;

                if (!string.IsNullOrWhiteSpace(s.PayloadJson))
                {
                    try {
                        using var doc = JsonDocument.Parse(s.PayloadJson);
                        if (doc.RootElement.TryGetProperty("sku", out var skuProp))
                        {
                            var skuId = skuProp.GetString();
                            var skuVariant = await _db.SkuVariants.FirstOrDefaultAsync(x => x.SkuId == skuId);
                            if (skuVariant != null)
                            {
                                var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == skuVariant.ProductModelId);
                                if (pm != null)
                                {
                                    productName = pm.ProductName ?? "";
                                    minStock = pm.MinStock;
                                }
                            }
                            currentStock = await _db.StockLedgers.Where(l => l.SkuId == skuId && l.Status == "GOOD").SumAsync(l => l.Quantity);
                            if (doc.RootElement.TryGetProperty("suggested", out var sugProp)) suggestedQty = sugProp.GetInt32();
                            if (doc.RootElement.TryGetProperty("diff", out var diffProp)) {
                                reason = $"Tăng/Giảm bất thường {Math.Round(diffProp.GetDouble() * 100)}%";
                                suggestedQty = doc.RootElement.GetProperty("avg7").GetInt32();
                            }
                        }
                    } catch {}
                }

                viewModels.Add(new {
                    Id = s.SuggestionId,
                    Type = s.SuggestionType,
                    Date = s.CreatedAt,
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
        public async Task<IActionResult> Approve(string id)
        {
            var suggestion = await _db.AiSuggestions.FirstOrDefaultAsync(s => s.SuggestionId == id);
            if (suggestion == null) return NotFound();

            suggestion.Status = "APPROVED";
            suggestion.ReviewedBy = User?.Identity?.Name;
            suggestion.ReviewedAt = DateTime.UtcNow;

            int suggestedQty = 0;
            string skuId = "";
            if (!string.IsNullOrWhiteSpace(suggestion.PayloadJson))
            {
                try {
                    using var doc = JsonDocument.Parse(suggestion.PayloadJson);
                    if (doc.RootElement.TryGetProperty("sku", out var skuProp)) skuId = skuProp.GetString() ?? "";
                    if (doc.RootElement.TryGetProperty("suggested", out var sugProp)) suggestedQty = sugProp.GetInt32();
                    if (doc.RootElement.TryGetProperty("avg7", out var avg7Prop)) suggestedQty = avg7Prop.GetInt32();
                } catch {}
            }

            // Sinh phiếu nhập nháp
            if (!string.IsNullOrWhiteSpace(skuId) && suggestedQty > 0)
            {
                var warehouse = await _db.Warehouses.FirstOrDefaultAsync();
                var voucher = new InventoryVoucher
                {
                    VoucherNumber = "PN-AI-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                    Date = DateTime.UtcNow,
                    Type = "INBOUND",
                    Status = "DRAFT",
                    WarehouseId = warehouse?.WarehouseId ?? "",
                    Reason = "Đề xuất tự động từ AI",
                    CreatedBy = User?.Identity?.Name,
                    Lines = new List<VoucherLine> {
                        new VoucherLine { SkuId = skuId, Quantity = suggestedQty, DocumentQuantity = suggestedQty, Unit = "Cái" }
                    }
                };
                _db.InventoryVouchers.Add(voucher);
                TempData["Message"] = "Đã duyệt đề xuất và tạo thành công Phiếu Nhập Kho (Nháp).";
            }
            else
            {
                TempData["Message"] = "Đã ghi nhận đề xuất từ AI.";
            }

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
            suggestion.ReviewedBy = User?.Identity?.Name;
            suggestion.ReviewedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            TempData["Message"] = "Đã từ chối đề xuất.";
            return RedirectToAction("Index");
        }
    }
}
