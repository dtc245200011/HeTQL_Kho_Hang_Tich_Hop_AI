using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DuAnCode.Web.Controllers;
using DuAnCode.Web.Models;
using DuAnCode.Web.Models.ViewModels;
using DuAnCode.Web.Repositories;
using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace DuAnCode.Tests
{
    public class OutboundWorkflowTests
    {
        [Fact]
        public async Task Approve_Outbound_Blocked_If_Overstock_ERR_STOCK_001()
        {
            // SRS-F-11 / ERR-STOCK-001: Chặn xuất vượt tồn
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            // WH-HN has only 20 of SKU-DESK-01. We request 50.
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-OVERSTOCK",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "YC-XUAT-001",
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", ProductName = "Bàn", Quantity = 50, UnitPrice = 5000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Step 1: Accountant approves
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            await controller.Approve(voucher.VoucherId);

            // Step 2: WarehouseManager attempts final approval
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result = await controller.Approve(voucher.VoucherId);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Contains("ERR-STOCK-001", controller.TempData["Error"]?.ToString() ?? "");

            // Stock should remain unchanged (20)
            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(20, ledger.Quantity);
        }

        [Fact]
        public async Task Outbound_Single_Sku_Deduction_Succeeds()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            // WH-HN has 20 of SKU-DESK-01. We export 5.
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-VALID-01",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "YC-XUAT-002",
                TotalAmount = 25000000m, // <= 100M
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", ProductName = "Bàn", Quantity = 5, UnitPrice = 5000000m, LineTotal = 25000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Step 1: Accountant
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            await controller.Approve(voucher.VoucherId);

            // Step 2: WarehouseManager
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            await controller.Approve(voucher.VoucherId);

            // Check final status
            var approvedVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", approvedVoucher!.Status);
            Assert.True(approvedVoucher.IsLocked);

            // Stock should be deducted: 20 - 5 = 15
            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(15, ledger.Quantity);

            // Outbound Movement logged with delta -5
            var movement = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId);
            Assert.NotNull(movement);
            Assert.Equal("OUTBOUND", movement.MovementType);
            Assert.Equal(-5, movement.QuantityDelta);
        }

        [Fact]
        public async Task Outbound_Over_100M_Requires_Director_Approval_Level_3()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            // Large order > 100M
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-LARGE-150M",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "HOP-DONG-150M",
                TotalAmount = 150000000m, // > 100M
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", ProductName = "Bàn", Quantity = 10, UnitPrice = 5000000m, LineTotal = 50000000m, BatchNumber = "" },
                    new VoucherLine { SkuId = "SKU-SOFA-01", ProductName = "Sofa", Quantity = 2, UnitPrice = 50000000m, LineTotal = 100000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Step 1: Accountant
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            await controller.Approve(voucher.VoucherId);

            // Step 2: WarehouseManager
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            await controller.Approve(voucher.VoucherId);

            // After Step 2, should still be PENDING because Level 3 Director is required!
            var voucherAfterL2 = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("PENDING", voucherAfterL2!.Status);
            Assert.False(voucherAfterL2.IsLocked);

            var steps = await db.ApprovalSteps.Where(s => s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
            Assert.Equal(3, steps.Count);
            Assert.Equal("APPROVED", steps[0].Status);
            Assert.Equal("APPROVED", steps[1].Status);
            Assert.Equal("PENDING", steps[2].Status);
            Assert.Equal("Director", steps[2].ApproverRole);

            // Step 3: Director approves
            TestHelper.SetUserContext(controller, "giamdoc1", "Director");
            await controller.Approve(voucher.VoucherId);

            var finalVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", finalVoucher!.Status);
            Assert.True(finalVoucher.IsLocked);
        }

        [Fact]
        public async Task Outbound_Combo_Deducts_BOM_Component_Stocks()
        {
            // SRS-F-11: Xuất Combo phải trừ tồn các cấu kiện BOM tương ứng
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            // In WH-HN: SKU-DESK-01 has 20, SKU-CHAIR-01 has 50.
            // COMBO-OFFICE-01 requires 1 Desk, 2 Chairs per set.
            // We export 3 sets of COMBO-OFFICE-01.
            // Expected deductions: Desk -3, Chair -6.
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-COMBO-01",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "YC-COMBO-ORDER",
                TotalAmount = 30000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "COMBO-OFFICE-01", ProductName = "Combo Bàn Ghế Văn Phòng", Quantity = 3, UnitPrice = 10000000m, LineTotal = 30000000m }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Step 1: Accountant
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            await controller.Approve(voucher.VoucherId);

            // Step 2: WarehouseManager
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            await controller.Approve(voucher.VoucherId);

            var finalVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", finalVoucher!.Status);

            // Check component stock deduction:
            var deskLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(17, deskLedger.Quantity); // 20 - 3 = 17

            var chairLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(44, chairLedger.Quantity); // 50 - 6 = 44

            // Check StockMovements created for each BOM component
            var deskMv = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId && m.SkuId == "SKU-DESK-01");
            Assert.NotNull(deskMv);
            Assert.Equal(-3, deskMv.QuantityDelta);

            var chairMv = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId && m.SkuId == "SKU-CHAIR-01");
            Assert.NotNull(chairMv);
            Assert.Equal(-6, chairMv.QuantityDelta);
        }

        [Fact]
        public async Task Outbound_CustomDirectorThreshold_Requires_DirectorApproval()
        {
            // Thay đổi cấu hình ngưỡng Giám đốc xuống 40.000.000 VNĐ thay vì 100.000.000 VNĐ mặc định
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            var config = await db.SystemConfigs.FirstAsync();
            config.OutboundDirectorApprovalThreshold = 40000000m;
            await db.SaveChangesAsync();

            // Phiếu có giá trị 50.000.000 VNĐ (> 40M) -> Bắt buộc sinh đủ 3 cấp duyệt gồm Giám đốc
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-CUSTOM-THRESH-01",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "YC-THRESH-01",
                TotalAmount = 50000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", ProductName = "Bàn", Quantity = 10, UnitPrice = 5000000m, LineTotal = 50000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Cấp 1: Kế toán duyệt
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            await controller.Approve(voucher.VoucherId);

            // Cấp 2: Quản lý kho duyệt
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            await controller.Approve(voucher.VoucherId);

            // Do vượt ngưỡng 40M đã cấu hình, phiếu vẫn phải ở trạng thái PENDING chờ Giám đốc
            var voucherAfterL2 = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("PENDING", voucherAfterL2!.Status);

            var steps = await db.ApprovalSteps.Where(s => s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
            Assert.Equal(3, steps.Count);
            Assert.Equal("APPROVED", steps[0].Status);
            Assert.Equal("APPROVED", steps[1].Status);
            Assert.Equal("PENDING", steps[2].Status);
            Assert.Equal("Director", steps[2].ApproverRole);

            // Cấp 3: Giám đốc duyệt
            TestHelper.SetUserContext(controller, "giamdoc1", "Director");
            await controller.Approve(voucher.VoucherId);

            var finalVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", finalVoucher!.Status);
            Assert.True(finalVoucher.IsLocked);
        }

        [Fact]
        public async Task Outbound_Cumulative_MultiLine_Overstock_Throws_Or_Rejects()
        {
            // Initial: SKU-SOFA-01 has stock = 2 in WH-HN.
            // A voucher with 2 separate lines (Qty=2 and Qty=1) totals 3.
            // Cumulative stock check must detect overstock and reject approval!
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);

            var config = await db.SystemConfigs.FirstAsync();
            config.EnableMultiLevelApproval = false; // direct 1-step approval for testing stock check
            await db.SaveChangesAsync();

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PX-OVERSTOCK-01",
                Date = DateTime.UtcNow,
                Type = "OUTBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "YC-OVER-01",
                TotalAmount = 75000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-SOFA-01", ProductName = "Sofa 1", Quantity = 2, UnitPrice = 25000000m, LineTotal = 50000000m, BatchNumber = "" },
                    new VoucherLine { SkuId = "SKU-SOFA-01", ProductName = "Sofa 2", Quantity = 1, UnitPrice = 25000000m, LineTotal = 25000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result = await controller.Approve(voucher.VoucherId);

            // Approval must fail due to insufficient cumulative stock (needs 3, available 2)
            Assert.Contains("ERR-STOCK-001", controller.TempData["Error"]?.ToString() ?? "");

            // Voucher is NOT approved, locked is false, stock unchanged
            var unapprovedVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.NotEqual("APPROVED", unapprovedVoucher!.Status);
            Assert.False(unapprovedVoucher.IsLocked);

            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-SOFA-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(2, ledger.Quantity);
        }

        [Fact]
        public async Task Outbound_AutoApprove_LowValue_WhenConfigured()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new OutboundController(db, repo, workflow);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            var config = await db.SystemConfigs.FirstAsync();
            config.AutoApproveLowValueOutbound = true;
            config.AutoApproveThreshold = 5000000m; // 5,000,000 VNĐ
            await db.SaveChangesAsync();

            var form = new VoucherFormViewModel
            {
                WarehouseId = "WH-HN",
                Counterparty = "Khách hàng mua lẻ",
                Address = "Hà Nội",
                Reason = "Bán lẻ",
                ReferenceDocument = "HD-00123",
                Lines = new List<VoucherLineInput>
                {
                    new() { SkuId = "SKU-SOFA-01", ProductName = "Sofa", Quantity = 1, UnitPrice = 1000000m }
                }
            };

            var result = await controller.Create(form);

            var createdVoucher = await db.InventoryVouchers.FirstAsync(v => v.Counterparty == "Khách hàng mua lẻ");
            Assert.Equal("APPROVED", createdVoucher.Status);
            Assert.True(createdVoucher.IsLocked);

            // Stock must be deducted from 2 down to 1
            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-SOFA-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(1, ledger.Quantity);
        }
    }
}
