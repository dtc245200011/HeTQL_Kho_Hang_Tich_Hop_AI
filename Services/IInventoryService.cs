using DuAnCode.Web.Models;

namespace DuAnCode.Web.Services
{
    public interface IInventoryService
    {
        Task<bool> AdjustStockAsync(string skuId, string warehouseId, string status, int delta, string performedBy, string referenceType, string referenceId);
        Task<StockLedger?> GetLedgerAsync(string skuId, string warehouseId, string status);
    }
}
