using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Repositories
{
    public class InventoryRepository : IInventoryRepository
    {
        private readonly ApplicationDbContext _db;
        public InventoryRepository(ApplicationDbContext db) { _db = db; }

        public async Task AddMovementAsync(StockMovement movement)
        {
            await _db.StockMovements.AddAsync(movement);
        }

        public async Task<StockLedger?> GetLedgerAsync(string skuId, string warehouseId, string status)
        {
            return await _db.StockLedgers.FirstOrDefaultAsync(x => x.SkuId == skuId && x.WarehouseId == warehouseId && x.Status == status);
        }

        public async Task<IEnumerable<StockLedger>> GetLedgersBySkuAsync(string skuId)
        {
            return await _db.StockLedgers.Where(x => x.SkuId == skuId).ToListAsync();
        }

        public async Task<List<StockLedger>> LockLedgersForUpdateAsync(IEnumerable<string> skuIds, string warehouseId, string status)
        {
            // Use UPDLOCK, ROWLOCK to acquire pessimistic locks in SQL Server
            var ids = string.Join("','", skuIds.Select(s => s.Replace("'", "''")));
            var sql = $"SELECT * FROM StockLedgers WITH (UPDLOCK, ROWLOCK) WHERE SkuId IN ('{ids}') AND WarehouseId = @p0 AND Status = @p1 ORDER BY SkuId ASC";
            return await _db.StockLedgers.FromSqlRaw(sql, warehouseId, status).ToListAsync();
        }

        public async Task UpdateLedgerAsync(StockLedger ledger)
        {
            _db.StockLedgers.Update(ledger);
            await Task.CompletedTask;
        }

        public async Task<int> SaveChangesAsync() => await _db.SaveChangesAsync();
    }
}
