using DuAnCode.Web.Services;
using DuAnCode.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DuAnCode.Web.Models.ViewModels;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class ReplenishmentController : Controller
    {
        private readonly IAiService _ai;
        private readonly ApplicationDbContext _db;
        public ReplenishmentController(IAiService ai, ApplicationDbContext db) { _ai = ai; _db = db; }

        public async Task<IActionResult> Index()
        {
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToDictionaryAsync(p => p.ProductModelId);
            var ledgers = await _db.StockLedgers.AsNoTracking().ToListAsync();

            var suggestions = skus.Select(sku =>
            {
                var total = ledgers.Where(l => l.SkuId == sku.SkuId).Sum(l => l.Quantity);
                pmMap.TryGetValue(sku.ProductModelId, out var pm);
                var min = pm?.MinStock ?? 0;
                var need = Math.Max(0, min - total);
                var priority = need >= (min * 0.7) ? "CAO" : need >= (min * 0.3) ? "TRUNG BÌNH" : "THẤP";
                return new ReplenishmentRow
                {
                    SkuId = sku.SkuId,
                    ProductName = pm?.ProductName ?? string.Empty,
                    CurrentStock = total,
                    MinStock = min,
                    Suggested = need,
                    Priority = priority
                };
            }).Where(x => x.Suggested > 0).ToList();

            return View(suggestions);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(string skuId)
        {
            // Create a simple inbound of suggested qty for the first warehouse found
            var sku = await _db.SkuVariants.FirstOrDefaultAsync(s => s.SkuId == skuId);
            if (sku == null) { TempData["Error"] = "SKU not found"; return RedirectToAction("Index"); }
            var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == sku.ProductModelId);
            var ledgers = await _db.StockLedgers.Where(l => l.SkuId == skuId && l.Status == "GOOD").ToListAsync();
            var total = ledgers.Sum(l => l.Quantity);
            var min = pm?.MinStock ?? 0;
            var need = Math.Max(0, min - total);
            if (need <= 0) { TempData["Message"] = "No replenishment needed."; return RedirectToAction("Index"); }

            var warehouse = await _db.Warehouses.FirstOrDefaultAsync();
            if (warehouse == null) { TempData["Error"] = "No warehouse configured."; return RedirectToAction("Index"); }

            var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouse.WarehouseId && l.Status == "GOOD");
            if (ledger == null)
            {
                ledger = new Models.StockLedger { SkuId = skuId, WarehouseId = warehouse.WarehouseId, Status = "GOOD", Quantity = 0 };
                _db.StockLedgers.Add(ledger);
            }

            ledger.Quantity += need;
            var mv = new Models.StockMovement
            {
                SkuId = skuId,
                WarehouseId = warehouse.WarehouseId,
                StockStatus = "GOOD",
                QuantityDelta = need,
                MovementType = "INBOUND",
                ReferenceType = "AI_APPROVE",
                ReferenceId = Guid.NewGuid().ToString(),
                PerformedBy = User?.Identity?.Name,
                CreatedAt = DateTime.UtcNow
            };
            _db.StockMovements.Add(mv);
            await _db.SaveChangesAsync();
            TempData["Message"] = $"Approved replenishment for {skuId}, qty {need}";
            return RedirectToAction("Index");
        }

        [Authorize]
        [HttpGet]
        public async Task<IActionResult> Suggest(string skuId)
        {
            var result = await _ai.SuggestReplenishmentAsync(skuId);
            return Json(new { message = result });
        }
    }
}

namespace DuAnCode.Web.Controllers
{
    public class ReplenishmentRow
    {
        public string SkuId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int CurrentStock { get; set; }
        public int MinStock { get; set; }
        public int Suggested { get; set; }
        public string Priority { get; set; } = string.Empty;
    }
}
