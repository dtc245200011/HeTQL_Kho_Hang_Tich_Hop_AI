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

        public async Task AddLedgerAsync(StockLedger ledger)
        {
            await _db.StockLedgers.AddAsync(ledger);
        }

        public async Task<IEnumerable<StockLedger>> GetLedgersBySkuAsync(string skuId)
        {
            return await _db.StockLedgers.Where(x => x.SkuId == skuId).ToListAsync();
        }

        public async Task<List<StockLedger>> LockLedgersForUpdateAsync(IEnumerable<string> skuIds, string warehouseId, string status)
        {
            // Create parameterized SQL dynamically
            var distinctIds = skuIds.Distinct().ToList();
            if (!distinctIds.Any()) return new List<StockLedger>();

            var paramNames = new List<string>();
            var parameters = new List<object>();
            for (int i = 0; i < distinctIds.Count; i++)
            {
                paramNames.Add($"@p{i}");
                parameters.Add(distinctIds[i]);
            }
            
            var pIndexWarehouse = distinctIds.Count;
            var pIndexStatus = distinctIds.Count + 1;
            parameters.Add(warehouseId);
            parameters.Add(status);

            var inClause = string.Join(", ", paramNames);
            var sql = $"SELECT * FROM StockLedgers WITH (UPDLOCK, ROWLOCK) WHERE SkuId IN ({inClause}) AND WarehouseId = @p{pIndexWarehouse} AND Status = @p{pIndexStatus} ORDER BY SkuId ASC";
            
            return await _db.StockLedgers.FromSqlRaw(sql, parameters.ToArray()).ToListAsync();
        }

        public async Task UpdateLedgerAsync(StockLedger ledger)
        {
            _db.StockLedgers.Update(ledger);
            await Task.CompletedTask;
        }

        public async Task<int> SaveChangesAsync() => await _db.SaveChangesAsync();
    }
}
