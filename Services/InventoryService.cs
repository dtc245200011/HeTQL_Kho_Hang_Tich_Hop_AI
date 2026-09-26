using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;

namespace DuAnCode.Web.Services
{
    public class InventoryService : IInventoryService
    {
        private readonly IInventoryRepository _repo;
        public InventoryService(IInventoryRepository repo) { _repo = repo; }

        public async Task<StockLedger?> GetLedgerAsync(string skuId, string warehouseId, string status)
        {
            return await _repo.GetLedgerAsync(skuId, warehouseId, status);
        }

        public async Task<bool> AdjustStockAsync(string skuId, string warehouseId, string status, int delta, string performedBy, string referenceType, string referenceId)
        {
            var ledger = await _repo.GetLedgerAsync(skuId, warehouseId, status);
            if (ledger == null)
            {
                ledger = new StockLedger { SkuId = skuId, WarehouseId = warehouseId, Status = status, Quantity = 0 };
                await _repo.GetLedgerAsync(skuId, warehouseId, status); // ensure tracked
            }

            ledger.Quantity += delta;
            if (ledger.Quantity < 0) return false;

            await _repo.UpdateLedgerAsync(ledger);

            var mv = new StockMovement
            {
                SkuId = skuId,
                WarehouseId = warehouseId,
                StockStatus = status,
                QuantityDelta = delta,
                MovementType = delta >= 0 ? "ADJUST_IN" : "ADJUST_OUT",
                ReferenceType = referenceType,
                ReferenceId = referenceId,
                PerformedBy = performedBy
            };
            await _repo.AddMovementAsync(mv);
            await _repo.SaveChangesAsync();
            return true;
        }
    }
}
