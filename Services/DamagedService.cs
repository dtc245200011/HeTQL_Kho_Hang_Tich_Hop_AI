using DuAnCode.Web.Models;
using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public interface IDamagedService
    {
        Task<bool> RecordDamagedAsync(string skuId, string warehouseId, int qty, string action, string performedBy, string? note = null);
        Task<bool> ChangeStatusDamagedAsync(string skuId, string warehouseId, int qty, string performedBy);
    }

    public class DamagedService : IDamagedService
    {
        private readonly ApplicationDbContext _db;
        public DamagedService(ApplicationDbContext db) { _db = db; }

        public async Task<bool> RecordDamagedAsync(string skuId, string warehouseId, int qty, string action, string performedBy, string? note = null)
        {
            var rec = new DamagedRecord { SkuId = skuId, WarehouseId = warehouseId, Quantity = qty, Action = action, Notes = note, CreatedBy = performedBy };
            _db.DamagedRecords.Add(rec);

            // Move qty from GOOD ledger to DAMAGED ledger
            var good = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouseId && l.Status == "GOOD");
            if (good == null || good.Quantity < qty) return false;
            good.Quantity -= qty;
            var damaged = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouseId && l.Status == "DAMAGED");
            if (damaged == null) { damaged = new StockLedger { SkuId = skuId, WarehouseId = warehouseId, Status = "DAMAGED", Quantity = 0 }; _db.StockLedgers.Add(damaged); }
            damaged.Quantity += qty;

            await _db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> ChangeStatusDamagedAsync(string skuId, string warehouseId, int qty, string performedBy)
        {
            // Only WarehouseManager can approve; this check is done at controller via policy
            // This method will attempt to move items from DAMAGED back to GOOD if repaired
            var damaged = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouseId && l.Status == "DAMAGED");
            if (damaged == null || damaged.Quantity < qty) return false;
            damaged.Quantity -= qty;
            var good = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == skuId && l.WarehouseId == warehouseId && l.Status == "GOOD");
            if (good == null) { good = new StockLedger { SkuId = skuId, WarehouseId = warehouseId, Status = "GOOD", Quantity = 0 }; _db.StockLedgers.Add(good); }
            good.Quantity += qty;
            await _db.SaveChangesAsync();
            return true;
        }
    }
}
