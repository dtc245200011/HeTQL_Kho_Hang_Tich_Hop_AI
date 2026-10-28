using DuAnCode.Web.Data;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class ReportController : Controller
    {
        private readonly ApplicationDbContext _db;
        public ReportController(ApplicationDbContext db) { _db = db; }

        private async Task<(List<ReportRow> Rows, List<Models.Warehouse> Warehouses, string WarehouseName)> GenerateReportDataAsync(DateTime? from, DateTime? to, string? warehouse)
        {
            var warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToDictionaryAsync(p => p.ProductModelId, p => p);
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
                
                var currentEnding = ledgers.Where(l => l.SkuId == sku.SkuId && ledgerWarehouseFilter(l)).Sum(l => l.Quantity);
                var currentDamaged = ledgers.Where(l => l.SkuId == sku.SkuId && (l.Status == "DAMAGED" || l.WarehouseId.Contains("DAMAGED")) && ledgerWarehouseFilter(l)).Sum(l => l.Quantity);
                
                var totalInAfter = stockMovements.Where(m => m.SkuId == sku.SkuId && m.QuantityDelta > 0 && m.CreatedAt > toDt && movementWarehouseFilter(m)).Sum(m => m.QuantityDelta);
                var totalOutAfter = stockMovements.Where(m => m.SkuId == sku.SkuId && m.QuantityDelta < 0 && m.CreatedAt > toDt && movementWarehouseFilter(m)).Sum(m => -m.QuantityDelta);

                var endingAtPeriod = currentEnding - totalInAfter + totalOutAfter;
                var beginningAtPeriod = endingAtPeriod - totalInPeriod + totalOutPeriod;

                var damInAfter = stockMovements.Where(m => m.SkuId == sku.SkuId && m.StockStatus == "DAMAGED" && m.QuantityDelta > 0 && m.CreatedAt > toDt && movementWarehouseFilter(m)).Sum(m => m.QuantityDelta);
                var damOutAfter = stockMovements.Where(m => m.SkuId == sku.SkuId && m.StockStatus == "DAMAGED" && m.QuantityDelta < 0 && m.CreatedAt > toDt && movementWarehouseFilter(m)).Sum(m => -m.QuantityDelta);
                var endingDamaged = Math.Max(0, currentDamaged - damInAfter + damOutAfter);
                var endingGood = Math.Max(0, endingAtPeriod - endingDamaged);

                var pm = pmMap.TryGetValue(sku.ProductModelId, out var p) ? p : null;

                return new ReportRow
                {
                    SkuId = sku.SkuId,
                    ProductName = pm?.ProductName ?? sku.SkuId,
                    Unit = pm?.Unit ?? "Cái",
                    Beginning = beginningAtPeriod,
                    TotalIn = totalInPeriod,
                    TotalOut = totalOutPeriod,
                    Ending = endingAtPeriod,
                    EndingGood = endingGood,
                    EndingDamaged = endingDamaged
                };
            }).ToList();

            var whName = warehouses.FirstOrDefault(w => w.WarehouseId == warehouse)?.WarehouseName ?? warehouse ?? "Tất cả các kho";
            return (reportRows, warehouses, whName);
        }

        [Authorize]
        public async Task<IActionResult> Index(DateTime? from, DateTime? to, string? warehouse)
        {
            ViewBag.From = from?.ToString("yyyy-MM-dd");
            ViewBag.To = to?.ToString("yyyy-MM-dd");
            ViewBag.SelectedWarehouse = warehouse;

            var (reportRows, warehouses, _) = await GenerateReportDataAsync(from, to, warehouse);

            var model = new ReportViewModel { Warehouses = warehouses, ReportRows = reportRows };
            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> ExportExcel(DateTime? from, DateTime? to, string? warehouse)
        {
            var (reportRows, _, whName) = await GenerateReportDataAsync(from, to, warehouse);
            var sb = new StringBuilder();
            sb.AppendLine("BÁO CÁO TỔNG HỢP NHẬP - XUẤT - TỒN (NXT)");
            sb.AppendLine($"Kho hàng:;{whName}");
            sb.AppendLine($"Thời gian:;{(from.HasValue ? from.Value.ToString("dd/MM/yyyy") : "Đầu")} đến {(to.HasValue ? to.Value.ToString("dd/MM/yyyy") : "Hiện tại")}");
            sb.AppendLine($"Ngày xuất báo cáo:;{DateTime.Now:dd/MM/yyyy HH:mm}");
            sb.AppendLine();
            sb.AppendLine("Mã SKU,Tên sản phẩm,ĐVT,Tồn đầu kỳ,Tổng Nhập,Tổng Xuất,Tồn cuối kỳ,Hàng tốt (GOOD),Hàng hỏng (DAMAGED)");

            foreach (var r in reportRows)
            {
                var escapedName = $"\"{r.ProductName.Replace("\"", "\"\"")}\"";
                sb.AppendLine($"{r.SkuId},{escapedName},{r.Unit},{r.Beginning},{r.TotalIn},{r.TotalOut},{r.Ending},{r.EndingGood},{r.EndingDamaged}");
            }

            var preamble = Encoding.UTF8.GetPreamble();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var fullBytes = preamble.Concat(bytes).ToArray();

            var filename = $"BaoCao_NXT_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            return File(fullBytes, "text/csv; charset=utf-8", filename);
        }
    }
}
