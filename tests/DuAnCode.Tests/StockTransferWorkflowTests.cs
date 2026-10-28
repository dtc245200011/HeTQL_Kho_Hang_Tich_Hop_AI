using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DuAnCode.Web.Controllers;
using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuAnCode.Tests
{
    public class StockTransferWorkflowTests
    {
        [Fact]
        public async Task Transfer_Fails_When_Source_And_Dest_Are_Same()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var controller = new StockTransferController(db, repo);
            TestHelper.SetUserContext(controller, "thukho1", "WarehouseKeeper");

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "CK-SAME-WH",
                Date = DateTime.UtcNow,
                Type = "TRANSFER",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ToWarehouseId = "WH-HN", // Same warehouse!
                ReferenceDocument = "LENH-DIEU-CHUYEN-01",
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", Quantity = 2, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            var result = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result);
            Assert.Contains("trùng nhau", controller.TempData["Error"]?.ToString() ?? "");
        }

        [Fact]
        public async Task Transfer_Succeeds_And_Updates_Both_Warehouses()
        {
            // BR-05: Chuyển kho trừ nguồn, cộng đích
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var controller = new StockTransferController(db, repo);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            // Initial: WH-HN has 50 SKU-CHAIR-01. WH-HCM has 15 SKU-CHAIR-01.
            // Transfer 10 chairs from WH-HN to WH-HCM.
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "CK-HN-TO-HCM",
                Date = DateTime.UtcNow,
                Type = "TRANSFER",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ToWarehouseId = "WH-HCM",
                ReferenceDocument = "LENH-DIEU-CHUYEN-02",
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-CHAIR-01", ProductName = "Ghế", Quantity = 10, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Step 1: Kế toán duyệt cấp 1
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            var result1 = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result1);

            // Step 2: Quản lý kho duyệt cấp 2 và thực hiện chuyển kho
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result2 = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result2);
            Assert.Null(controller.TempData["Error"]?.ToString());

            // Source stock: 50 - 10 = 40
            var sourceLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(40, sourceLedger.Quantity);

            // Dest stock: 15 + 10 = 25
            var destLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HCM");
            Assert.Equal(25, destLedger.Quantity);

            // Verify movements: TRANSFER_OUT (-10) and TRANSFER_IN (+10)
            var outMv = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId && m.MovementType == "TRANSFER_OUT");
            Assert.NotNull(outMv);
            Assert.Equal(-10, outMv.QuantityDelta);

            var inMv = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId && m.MovementType == "TRANSFER_IN");
            Assert.NotNull(inMv);
            Assert.Equal(10, inMv.QuantityDelta);
        }
    }
}
