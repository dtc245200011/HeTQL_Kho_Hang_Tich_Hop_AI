using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public class AiService : IAiService
    {
        private readonly ApplicationDbContext _db;
        public AiService(ApplicationDbContext db) { _db = db; }

        public async Task<string> SuggestReplenishmentAsync(string skuId)
        {
            var sku = await _db.SkuVariants.FirstOrDefaultAsync(s => s.SkuId == skuId);
            if (sku == null) return "SKU not found";

            var total = await _db.StockLedgers.Where(x => x.SkuId == skuId).SumAsync(x => (int?)x.Quantity) ?? 0;
            var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == sku.ProductModelId);
            var min = pm?.MinStock ?? 0;
            if (total < min)
            {
                var need = min - total;
                return $"Suggest reorder {need} units for {skuId} to reach min stock {min}.";
            }
            return "Stock level adequate.";
        }

        public async Task<IEnumerable<dynamic>> GetAllSkusAsync()
        {
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            return skus.Select(s => (dynamic)s).ToList();
        }

        public async Task<IEnumerable<dynamic>> GetAllLedgersAsync()
        {
            var ledgers = await _db.StockLedgers.AsNoTracking().ToListAsync();
            return ledgers.Select(l => (dynamic)l).ToList();
        }
    }
}
