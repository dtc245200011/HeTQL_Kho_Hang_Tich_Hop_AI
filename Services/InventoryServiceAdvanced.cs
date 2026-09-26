using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public interface IInventoryServiceAdvanced
    {
        Task<int> GetAvailableStockAsync(string skuId, string warehouseId);
        Task<int> GetComboAvailableAsync(string comboId, string warehouseId);
        Task<(bool ok, string? error)> LockAndDeductAsync(Dictionary<string,int> skuDeductions, string warehouseId, string performedBy, string referenceType, string referenceId);
    }

    public class InventoryServiceAdvanced : IInventoryServiceAdvanced
    {
        private readonly IInventoryRepository _repo;
        private readonly ApplicationDbContext _db;
        public InventoryServiceAdvanced(IInventoryRepository repo, ApplicationDbContext db) { _repo = repo; _db = db; }

        public async Task<int> GetAvailableStockAsync(string skuId, string warehouseId)
        {
            var ledgers = await _repo.GetLedgersBySkuAsync(skuId);
            var good = ledgers.Where(l => l.WarehouseId == warehouseId && l.Status == "GOOD").Sum(l => l.Quantity);
            // Locked quantity not tracked separately here; assume IsLocked/LockedByTxn used in other flows
            return good;
        }

        public async Task<int> GetComboAvailableAsync(string comboId, string warehouseId)
        {
            var combo = await _db.ComboProducts.Include(c => c.Components).FirstOrDefaultAsync(c => c.ComboId == comboId);
            if (combo == null) return 0;
            int minSets = int.MaxValue;
            foreach(var c in combo.Components)
            {
                var avail = await GetAvailableStockAsync(c.SkuId, warehouseId);
                var sets = avail / Math.Max(1, c.QuantityPerSet);
                minSets = Math.Min(minSets, sets);
            }
            return minSets == int.MaxValue ? 0 : minSets;
        }

        public async Task<(bool ok, string? error)> LockAndDeductAsync(Dictionary<string,int> skuDeductions, string warehouseId, string performedBy, string referenceType, string referenceId)
        {
            // Implements pessimistic locking and deduction in a single DbTransaction
            using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                var skuIds = skuDeductions.Keys.OrderBy(k => k).ToList();
                var lockedLedgers = await _repo.LockLedgersForUpdateAsync(skuIds, warehouseId, "GOOD");

                // Ensure all SKUs have ledger rows
                foreach(var sku in skuIds)
                {
                    var l = lockedLedgers.FirstOrDefault(x => x.SkuId == sku);
                    if (l == null)
                    {
                        // create new ledger
                        l = new StockLedger { SkuId = sku, WarehouseId = warehouseId, Status = "GOOD", Quantity = 0 };
                        _db.StockLedgers.Add(l);
                        lockedLedgers.Add(l);
                    }
                }

                // Check availability
                foreach(var kv in skuDeductions)
                {
                    var sku = kv.Key; var qty = kv.Value;
                    var ledger = lockedLedgers.First(x => x.SkuId == sku && x.WarehouseId == warehouseId && x.Status == "GOOD");
                    if (ledger.Quantity < qty)
                    {
                        await tx.RollbackAsync();
                        return (false, "ERR-STOCK-001: Không đủ tồn để trừ");
                    }
                    ledger.Quantity -= qty;
                    _db.StockMovements.Add(new StockMovement { SkuId = sku, WarehouseId = warehouseId, StockStatus = "GOOD", QuantityDelta = -qty, MovementType = "OUTBOUND", ReferenceType = referenceType, ReferenceId = referenceId, PerformedBy = performedBy, CreatedAt = DateTime.UtcNow });
                }

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
                return (true, null);
            }
            catch(Exception ex)
            {
                try { await tx.RollbackAsync(); } catch { }
                return (false, "ERR-STOCK-002: Lỗi khi cập nhật tồn: " + ex.Message);
            }
        }
    }
}
