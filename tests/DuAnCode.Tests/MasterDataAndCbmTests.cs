using System;
using System.Linq;
using System.Threading.Tasks;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuAnCode.Tests
{
    public class MasterDataAndCbmTests
    {
        [Theory]
        [InlineData(1200, 600, 750, 0.54)]
        [InlineData(600, 600, 1000, 0.36)]
        [InlineData(2000, 900, 800, 1.44)]
        [InlineData(1600, 800, 750, 0.96)]
        public void CbmCalculation_Matches_Formula(int lengthMm, int widthMm, int heightMm, decimal expectedCbm)
        {
            // SRS-F-02: CBM (m3) = (Dai_mm * Rong_mm * Cao_mm) / 1,000,000,000
            decimal calculatedCbm = Math.Round((decimal)lengthMm * (decimal)widthMm * (decimal)heightMm / 1000000000m, 6);
            Assert.Equal(expectedCbm, calculatedCbm);
        }

        [Fact]
        public async Task Combo_Available_Stock_Is_Derived_From_Bom_Components()
        {
            // SRS-F-10: Tồn khả dụng Combo = MIN( floor(Tồn khả dụng cấu kiện / định mức) )
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new DuAnCode.Web.Repositories.InventoryRepository(db);
            var advancedService = new InventoryServiceAdvanced(repo, db);

            // WH-HN has: SKU-DESK-01 = 20, SKU-CHAIR-01 = 50.
            // Combo COMBO-OFFICE-01 requires: 1 SKU-DESK-01, 2 SKU-CHAIR-01.
            // Available combos = min(20/1, 50/2) = min(20, 25) = 20
            var availableCombos = await advancedService.GetComboAvailableAsync("COMBO-OFFICE-01", "WH-HN");
            Assert.Equal(20, availableCombos);

            // If we reduce SKU-CHAIR-01 stock to 10 in WH-HN:
            var chairLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HN");
            chairLedger.Quantity = 10;
            await db.SaveChangesAsync();

            // Now: min(20/1, 10/2) = min(20, 5) = 5 sets (bottleneck is Chair)
            availableCombos = await advancedService.GetComboAvailableAsync("COMBO-OFFICE-01", "WH-HN");
            Assert.Equal(5, availableCombos);
        }

        [Fact]
        public async Task Total_Cbm_Per_Sku_Calculates_Correctly()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var sku = await db.SkuVariants.FirstAsync(s => s.SkuId == "SKU-DESK-01");
            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");

            // Total CBM = Unit CBM * Good Quantity
            decimal totalCbm = sku.Cbm * ledger.Quantity;
            Assert.Equal(0.96m * 20, totalCbm);
        }

        [Fact]
        public async Task StopApi_Blocks_External_Access_With_503()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var config = await db.SystemConfigs.FirstAsync();
            config.StopApi = true;
            await db.SaveChangesAsync();

            var controller = new DuAnCode.Web.Controllers.QuickSearchController(db);
            TestHelper.SetUserContext(controller, "guest_client", "Staff"); // Non-admin

            var result = await controller.GetAvailableStock("WH-HN", null);
            var statusResult = Assert.IsType<Microsoft.AspNetCore.Mvc.ObjectResult>(result);
            Assert.Equal(503, statusResult.StatusCode);
        }
    }
}
