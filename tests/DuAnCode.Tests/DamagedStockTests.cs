using System.Threading.Tasks;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuAnCode.Tests
{
    public class DamagedStockTests
    {
        [Fact]
        public async Task Record_Damaged_Transfers_Good_To_Damaged_Status()
        {
            // SRS-F-12: Chuyển tồn từ GOOD sang DAMAGED khi phát hiện lỗi
            using var db = TestHelper.CreateInMemoryDbContext();
            var service = new DamagedService(db);

            // In WH-HN: SKU-DESK-01 has 20 GOOD stock, 0 DAMAGED stock.
            // Move 3 units to DAMAGED
            var success = await service.RecordDamagedAsync("SKU-DESK-01", "WH-HN", 3, "INSPECTION", "thukho1", "Mặt bàn trầy xước");
            Assert.True(success);

            var goodLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN" && l.Status == "GOOD");
            Assert.Equal(17, goodLedger.Quantity); // 20 - 3 = 17

            var damagedLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN" && l.Status == "DAMAGED");
            Assert.Equal(3, damagedLedger.Quantity);

            // Check DamagedRecord was created
            var record = await db.DamagedRecords.FirstOrDefaultAsync(r => r.SkuId == "SKU-DESK-01");
            Assert.NotNull(record);
            Assert.Equal(3, record.Quantity);
        }

        [Fact]
        public async Task Record_Damaged_Fails_When_Insufficient_Good_Stock()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var service = new DamagedService(db);

            // In WH-HN: SKU-SOFA-01 has only 2 units. Request 5 units to damaged.
            var success = await service.RecordDamagedAsync("SKU-SOFA-01", "WH-HN", 5, "INSPECTION", "thukho1");
            Assert.False(success);

            // Good stock must remain untouched
            var goodLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-SOFA-01" && l.WarehouseId == "WH-HN" && l.Status == "GOOD");
            Assert.Equal(2, goodLedger.Quantity);
        }

        [Fact]
        public async Task Restore_Damaged_To_Good_Status_After_Repair()
        {
            // SRS-F-13: Chuyển lại về GOOD sau khi đã sửa chữa xong
            using var db = TestHelper.CreateInMemoryDbContext();
            var service = new DamagedService(db);

            // In WH-HN: Move 2 units of SKU-DESK-01 to DAMAGED first
            await service.RecordDamagedAsync("SKU-DESK-01", "WH-HN", 2, "INSPECTION", "thukho1");

            // Now restore 2 units back to GOOD
            var success = await service.ChangeStatusDamagedAsync("SKU-DESK-01", "WH-HN", 2, "quanly1");
            Assert.True(success);

            var goodLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN" && l.Status == "GOOD");
            Assert.Equal(20, goodLedger.Quantity);

            var damagedLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN" && l.Status == "DAMAGED");
            Assert.Equal(0, damagedLedger.Quantity);
        }
    }
}
