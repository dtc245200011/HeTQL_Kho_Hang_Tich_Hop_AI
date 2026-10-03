using DuAnCode.Web.Models;
using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;

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
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var ledgers = await _db.StockLedgers
                    .FromSqlRaw("SELECT * FROM StockLedgers WITH (UPDLOCK, ROWLOCK) WHERE SkuId = {0} AND WarehouseId = {1} AND Status IN ('GOOD', 'DAMAGED')", skuId, warehouseId)
                    .ToListAsync();

                var good = ledgers.FirstOrDefault(l => l.Status == "GOOD");
                if (good == null || good.Quantity < qty) 
                {
                    await transaction.RollbackAsync();
                    return false;
                }
                
                good.Quantity -= qty;

                var damaged = ledgers.FirstOrDefault(l => l.Status == "DAMAGED");
                if (damaged == null) 
                { 
                    damaged = new StockLedger { SkuId = skuId, WarehouseId = warehouseId, Status = "DAMAGED", Quantity = 0 }; 
                    _db.StockLedgers.Add(damaged); 
                }
                damaged.Quantity += qty;

                var rec = new DamagedRecord { SkuId = skuId, WarehouseId = warehouseId, Quantity = qty, Action = action, Notes = note, CreatedBy = performedBy };
                _db.DamagedRecords.Add(rec);

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> ChangeStatusDamagedAsync(string skuId, string warehouseId, int qty, string performedBy)
        {
            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var ledgers = await _db.StockLedgers
                    .FromSqlRaw("SELECT * FROM StockLedgers WITH (UPDLOCK, ROWLOCK) WHERE SkuId = {0} AND WarehouseId = {1} AND Status IN ('GOOD', 'DAMAGED')", skuId, warehouseId)
                    .ToListAsync();

                var damaged = ledgers.FirstOrDefault(l => l.Status == "DAMAGED");
                if (damaged == null || damaged.Quantity < qty) 
                {
                    await transaction.RollbackAsync();
                    return false;
                }
                
                damaged.Quantity -= qty;

                var good = ledgers.FirstOrDefault(l => l.Status == "GOOD");
                if (good == null) 
                { 
                    good = new StockLedger { SkuId = skuId, WarehouseId = warehouseId, Status = "GOOD", Quantity = 0 }; 
                    _db.StockLedgers.Add(good); 
                }
                good.Quantity += qty;

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                return true;
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
