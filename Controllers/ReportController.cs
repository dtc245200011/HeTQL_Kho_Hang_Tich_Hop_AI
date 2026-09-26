using DuAnCode.Web.Data;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ApplicationDbContext _db;
        public ReportController(ApplicationDbContext db) { _db = db; }

        [Authorize]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? warehouse)
        {
            // Prepare simple report rows and warehouses to pass to the view
            var warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var stockMovements = await _db.StockMovements.AsNoTracking().ToListAsync();
            var ledgers = await _db.StockLedgers.AsNoTracking().ToListAsync();

            // Normalize date range (use entire day for 'to')
            DateTime fromDt = from?.Date ?? DateTime.MinValue;
            DateTime toDt = (to?.Date.AddDays(1).AddTicks(-1)) ?? DateTime.MaxValue;

            // Apply warehouse filter when requested
            Func<Models.StockMovement, bool> movementWarehouseFilter = m => string.IsNullOrEmpty(warehouse) || m.WarehouseId == warehouse;
            Func<Models.StockLedger, bool> ledgerWarehouseFilter = l => string.IsNullOrEmpty(warehouse) || l.WarehouseId == warehouse;

            var reportRows = skus.Select(sku =>
            {
                var totalInPeriod = stockMovements.Where(m => m.SkuId == sku.SkuId && m.QuantityDelta > 0 && m.CreatedAt >= fromDt && m.CreatedAt <= toDt && movementWarehouseFilter(m)).Sum(m => m.QuantityDelta);
                var totalOutPeriod = stockMovements.Where(m => m.SkuId == sku.SkuId && m.QuantityDelta < 0 && m.CreatedAt >= fromDt && m.CreatedAt <= toDt && movementWarehouseFilter(m)).Sum(m => -m.QuantityDelta);
                var ending = ledgers.Where(l => l.SkuId == sku.SkuId && ledgerWarehouseFilter(l)).Sum(l => l.Quantity);
                var beginning = ending - totalInPeriod + totalOutPeriod;
                return new ReportRow
                {
                    SkuId = sku.SkuId,
                    Beginning = beginning,
                    TotalIn = totalInPeriod,
                    TotalOut = totalOutPeriod,
                    Ending = ending
                };
            }).ToList();

            var model = new ReportViewModel { Warehouses = warehouses, ReportRows = reportRows };
            return View(model);
        }
    }
}
