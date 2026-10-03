using DuAnCode.Web.Models;

namespace DuAnCode.Web.Repositories
{
    public interface IInventoryRepository
    {
        Task<StockLedger?> GetLedgerAsync(string skuId, string warehouseId, string status);
        Task<IEnumerable<StockLedger>> GetLedgersBySkuAsync(string skuId);
        Task AddMovementAsync(StockMovement movement);
        Task AddLedgerAsync(StockLedger ledger);
        Task UpdateLedgerAsync(StockLedger ledger);
        Task<int> SaveChangesAsync();
        Task<List<StockLedger>> LockLedgersForUpdateAsync(IEnumerable<string> skuIds, string warehouseId, string status);
    }
}
