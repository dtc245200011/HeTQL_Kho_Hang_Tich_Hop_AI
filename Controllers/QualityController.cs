using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class QualityController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly DuAnCode.Web.Services.IDamagedService _damagedService;

        public QualityController(ApplicationDbContext db, DuAnCode.Web.Services.IDamagedService? damagedService = null)
        {
            _db = db;
            _damagedService = damagedService ?? new DuAnCode.Web.Services.DamagedService(db);
        }

        public async Task<IActionResult> Index(string? search = null)
        {
            var query = _db.StockLedgers.AsNoTracking()
                .Where(sl => (sl.Status == "DAMAGED" || sl.WarehouseId.Contains("DAMAGED")) && sl.Quantity > 0)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(sl => sl.SkuId.Contains(s) || sl.BatchNumber.Contains(s));
            }

            var damagedList = await query.ToListAsync();
            var skuIds = damagedList.Select(x => x.SkuId).Distinct().ToList();

            var skus = await _db.SkuVariants.AsNoTracking().Where(s => skuIds.Contains(s.SkuId)).ToListAsync();
            var allSkus = await _db.SkuVariants.AsNoTracking().Where(s => s.IsActive).ToListAsync();
            var pmIds = skus.Select(s => s.ProductModelId).Union(allSkus.Select(s => s.ProductModelId)).Distinct().ToList();
            var pmMap = await _db.ProductModels.AsNoTracking().Where(p => pmIds.Contains(p.ProductModelId)).ToDictionaryAsync(p => p.ProductModelId, p => p);
            var whMap = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.WarehouseId, w => w.WarehouseName);
            var allWarehouses = await _db.Warehouses.AsNoTracking().Where(w => w.IsActive).ToListAsync();

            ViewBag.Skus = skus.ToDictionary(s => s.SkuId, s => s);
            ViewBag.AllSkus = allSkus;
            ViewBag.ProductModels = pmMap;
            ViewBag.WarehouseMap = whMap;
            ViewBag.AllWarehouses = allWarehouses;
            ViewBag.Search = search;

            // Load recent damaged records
            ViewBag.RecentRecords = await _db.DamagedRecords.AsNoTracking().OrderByDescending(r => r.CreatedAt).Take(15).ToListAsync();

            return View(damagedList);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Director,WarehouseManager,QC,Thủ kho,Kiểm định chất lượng (QC),Quản lý kho,Quản trị viên")]
        public async Task<IActionResult> RecordDamaged(string skuId, string warehouseId, int quantity, string? action, string? note)
        {
            if (string.IsNullOrWhiteSpace(skuId) || string.IsNullOrWhiteSpace(warehouseId) || quantity <= 0)
            {
                TempData["Error"] = "Dữ liệu báo hỏng không hợp lệ.";
                return RedirectToAction("Index");
            }

            var success = await _damagedService.RecordDamagedAsync(skuId, warehouseId, quantity, string.IsNullOrWhiteSpace(action) ? "QC_INSPECTION" : action, User?.Identity?.Name ?? "QC", note);
            if (!success)
            {
                TempData["Error"] = $"Không thể báo hỏng {quantity} sản phẩm {skuId}. Tồn kho khả dụng (GOOD) tại kho không đủ!";
            }
            else
            {
                TempData["Message"] = $"Đã ghi nhận và cách ly {quantity} sản phẩm {skuId} sang trạng thái hàng lỗi thành công!";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Director,WarehouseManager,Quản lý kho,Quản trị viên,Giám đốc")]
        public async Task<IActionResult> RestoreGood(string skuId, string warehouseId, int quantity)
        {
            if (string.IsNullOrWhiteSpace(skuId) || string.IsNullOrWhiteSpace(warehouseId) || quantity <= 0)
            {
                TempData["Error"] = "Dữ liệu phục hồi không hợp lệ.";
                return RedirectToAction("Index");
            }

            var success = await _damagedService.ChangeStatusDamagedAsync(skuId, warehouseId, quantity, User?.Identity?.Name ?? "Manager");
            if (!success)
            {
                TempData["Error"] = $"Không thể phục hồi {quantity} sản phẩm {skuId}. Số lượng hàng lỗi hiện tại không đủ!";
            }
            else
            {
                TempData["Message"] = $"Đã phục hồi {quantity} sản phẩm {skuId} sau sửa chữa về trạng thái Hàng tốt (GOOD) thành công!";
            }
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Director,WarehouseManager,Quản lý kho,Quản trị viên,Giám đốc")]
        public async Task<IActionResult> DisposeDamaged(string skuId, string warehouseId, int quantity, string? reason)
        {
            if (string.IsNullOrWhiteSpace(skuId) || string.IsNullOrWhiteSpace(warehouseId) || quantity <= 0)
            {
                TempData["Error"] = "Dữ liệu thanh lý không hợp lệ.";
                return RedirectToAction("Index");
            }

            var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouseId && (l.Status == "DAMAGED" || l.WarehouseId.Contains("DAMAGED")));
            if (ledger == null || ledger.Quantity < quantity)
            {
                TempData["Error"] = $"Không đủ số lượng hàng lỗi {skuId} để thanh lý.";
                return RedirectToAction("Index");
            }

            ledger.Quantity -= quantity;
            _db.StockMovements.Add(new StockMovement
            {
                SkuId = skuId,
                WarehouseId = warehouseId,
                StockStatus = "DAMAGED",
                QuantityDelta = -quantity,
                MovementType = "DAMAGED_DISPOSAL",
                ReferenceType = "DISPOSAL",
                ReferenceId = "DISPOSE-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"),
                PerformedBy = User?.Identity?.Name ?? "System",
                CreatedAt = DateTime.UtcNow
            });

            _db.DamagedRecords.Add(new DamagedRecord
            {
                SkuId = skuId,
                WarehouseId = warehouseId,
                Quantity = -quantity,
                Action = "DISPOSAL",
                Notes = reason ?? "Thanh lý / Tiêu hủy hàng hỏng không thể sửa chữa",
                CreatedBy = User?.Identity?.Name ?? "System"
            });

            await _db.SaveChangesAsync();
            TempData["Message"] = $"Đã xuất thanh lý {quantity} sản phẩm lỗi {skuId} thành công!";
            return RedirectToAction("Index");
        }
    }
}
