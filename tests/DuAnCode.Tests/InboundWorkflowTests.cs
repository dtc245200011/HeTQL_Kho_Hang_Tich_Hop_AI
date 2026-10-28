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
    public class InboundWorkflowTests
    {
        [Fact]
        public async Task Create_Inbound_Draft_Success()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);
            TestHelper.SetUserContext(controller, "nhanvien1", "Staff");

            var form = new VoucherFormViewModel
            {
                VoucherNumber = "AUTO",
                Date = DateTime.UtcNow,
                WarehouseId = "WH-HN",
                Counterparty = "NCC Nội Thất 1",
                ReferenceDocument = "HD-NCC-001",
                Reason = "Nhập hàng theo hợp đồng",
                Lines = new List<VoucherLineInput>
                {
                    new VoucherLineInput
                    {
                        SkuId = "SKU-DESK-01",
                        ProductName = "Bàn làm việc",
                        Unit = "Cái",
                        Quantity = 10,
                        DocumentQuantity = 10,
                        UnitPrice = 5000000m
                    }
                }
            };

            var result = await controller.Create(form);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);

            var voucher = await db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Type == "INBOUND" && v.ReferenceDocument == "HD-NCC-001");
            Assert.NotNull(voucher);
            Assert.Equal("DRAFT", voucher.Status);
            Assert.False(voucher.IsLocked);
            Assert.Single(voucher.Lines);
            Assert.Equal(50000000m, voucher.TotalAmount);
        }

        [Fact]
        public async Task Approve_Inbound_Fails_Without_ReferenceDocument_BR01()
        {
            // BR-01: Phiếu nhập chỉ được duyệt khi có Chứng từ gốc
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PN-TEST-001",
                Date = DateTime.UtcNow,
                Type = "INBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "", // Empty reference document
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", Quantity = 5, UnitPrice = 5000000m, LineTotal = 25000000m }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            var result = await controller.Approve(voucher.VoucherId);
            var redirect = Assert.IsType<RedirectToActionResult>(result);

            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("BR-01", controller.TempData["Error"]?.ToString() ?? "");

            // Status remains DRAFT
            var reloaded = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("DRAFT", reloaded!.Status);
        }

        [Fact]
        public async Task Inbound_Two_Step_Approval_Progression_And_Stock_Update()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PN-TEST-MULTI",
                Date = DateTime.UtcNow,
                Type = "INBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "HD-VALID-01",
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", Quantity = 10, UnitPrice = 5000000m, LineTotal = 50000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Initial stock in WH-HN for SKU-DESK-01 is 20
            var initialLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(20, initialLedger.Quantity);

            // Step 1: Accountant approves
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            var result1 = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result1);

            // Voucher should now be PENDING, step 1 APPROVED, step 2 PENDING
            var voucherAfterStep1 = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("PENDING", voucherAfterStep1!.Status);
            Assert.False(voucherAfterStep1.IsLocked);

            var steps = await db.ApprovalSteps.Where(s => s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
            Assert.Equal(2, steps.Count);
            Assert.Equal("APPROVED", steps[0].Status); // Level 1 (Accountant)
            Assert.Equal("PENDING", steps[1].Status);  // Level 2 (WarehouseManager)

            // Stock should NOT have updated yet!
            var ledgerAfterStep1 = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(20, ledgerAfterStep1.Quantity);

            // Step 2 Unauthorized attempt: WarehouseKeeper tries to approve Level 2
            TestHelper.SetUserContext(controller, "thukho1", "WarehouseKeeper");
            var unauthResult = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(unauthResult);
            Assert.Contains("Bạn không có quyền duyệt", controller.TempData["Error"]?.ToString() ?? "");

            // Step 2 Authorized: WarehouseManager approves
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result2 = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result2);

            // Voucher should now be APPROVED & LOCKED
            var finalVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", finalVoucher!.Status);
            Assert.True(finalVoucher.IsLocked);

            // Stock should be updated: 20 + 10 = 30
            var finalLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(30, finalLedger.Quantity);

            // Stock movement must be logged
            var movement = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == voucher.VoucherId);
            Assert.NotNull(movement);
            Assert.Equal("INBOUND", movement.MovementType);
            Assert.Equal(10, movement.QuantityDelta);

            // AuditLog must be created
            var auditLog = await db.AuditLogs.FirstOrDefaultAsync(a => a.EntityId == voucher.VoucherId && a.Action == "APPROVE");
            Assert.NotNull(auditLog);
        }

        [Fact]
        public async Task Create_Inbound_Blocked_When_StopInbound_Or_MaintenanceMode()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);
            TestHelper.SetUserContext(controller, "nhanvien1", "Staff");

            var config = await db.SystemConfigs.FirstAsync();
            config.StopInbound = true;
            await db.SaveChangesAsync();

            var form = new VoucherFormViewModel
            {
                WarehouseId = "WH-HN",
                Lines = new List<VoucherLineInput> { new VoucherLineInput { SkuId = "SKU-DESK-01", Quantity = 1 } }
            };

            var result = await controller.Create(form);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            Assert.Contains("tạm dừng", controller.TempData["Error"]?.ToString() ?? "");
        }

        [Fact]
        public async Task Inbound_SingleLevel_When_MultiLevelDisabled()
        {
            // Khi tắt phê duyệt nhiều cấp (EnableMultiLevelApproval = false), phiếu chỉ cần Quản lý kho duyệt 1 lần là hoàn tất
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);

            var config = await db.SystemConfigs.FirstAsync();
            config.EnableMultiLevelApproval = false;
            await db.SaveChangesAsync();

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PN-SINGLE-01",
                Date = DateTime.UtcNow,
                Type = "INBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "HD-SINGLE-001",
                TotalAmount = 25000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", Quantity = 5, UnitPrice = 5000000m, LineTotal = 25000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Duyệt bởi Quản lý kho
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result);

            // Kiểm tra: Chỉ có 1 bước duyệt duy nhất và phiếu chuyển sang APPROVED ngay lập tức!
            var steps = await db.ApprovalSteps.Where(s => s.EntityId == voucher.VoucherId).ToListAsync();
            Assert.Single(steps);
            Assert.Equal("APPROVED", steps[0].Status);

            var finalVoucher = await db.InventoryVouchers.FindAsync(voucher.VoucherId);
            Assert.Equal("APPROVED", finalVoucher!.Status);
            Assert.True(finalVoucher.IsLocked);
        }

        [Fact]
        public async Task Inbound_Custom_Approver_Role_Configuration()
        {
            // Khi cấu hình Cấp 1 là QC thay vì Kế toán
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);

            var config = await db.SystemConfigs.FirstAsync();
            config.ApprovalRoleLevel1 = "QC";
            await db.SaveChangesAsync();

            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PN-QC-01",
                Date = DateTime.UtcNow,
                Type = "INBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "HD-QC-001",
                TotalAmount = 10000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "SKU-DESK-01", Quantity = 2, UnitPrice = 5000000m, LineTotal = 10000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            // Nếu Kế toán vào duyệt thì sẽ bị từ chối vì cấu hình yêu cầu QC
            TestHelper.SetUserContext(controller, "ketoan1", "Accountant");
            var resFail = await controller.Approve(voucher.VoucherId);
            Assert.Contains("Kiểm định chất lượng (QC)", controller.TempData["Error"]?.ToString() ?? "");

            // Nhân viên QC vào duyệt thành công cấp 1
            TestHelper.SetUserContext(controller, "qc1", "QC");
            var resSuccess = await controller.Approve(voucher.VoucherId);
            Assert.Contains("Đã ký duyệt cấp 1 thành công", controller.TempData["Message"]?.ToString() ?? "");
        }

        [Fact]
        public async Task Inbound_Explodes_Combo_BOM_Into_Components()
        {
            // Combo product in Inbound must explode BOM into components (SKU-DESK-01 & SKU-CHAIR-01)
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var workflow = new ApprovalWorkflowService(db);
            var controller = new InboundController(db, repo, workflow);

            var config = await db.SystemConfigs.FirstAsync();
            config.EnableMultiLevelApproval = false; // direct 1-step approval for simplicity
            await db.SaveChangesAsync();

            // Initial: SKU-DESK-01 = 20, SKU-CHAIR-01 = 50 in WH-HN
            // COMBO-OFFICE-01 consists of 1 DESK and 2 CHAIR
            // Inbound 5 units of COMBO-OFFICE-01 -> +5 DESK, +10 CHAIR
            var voucher = new InventoryVoucher
            {
                VoucherNumber = "PN-COMBO-01",
                Date = DateTime.UtcNow,
                Type = "INBOUND",
                Status = "DRAFT",
                WarehouseId = "WH-HN",
                ReferenceDocument = "HD-COMBO-01",
                TotalAmount = 50000000m,
                Lines = new List<VoucherLine>
                {
                    new VoucherLine { SkuId = "COMBO-OFFICE-01", Quantity = 5, UnitPrice = 10000000m, LineTotal = 50000000m, BatchNumber = "" }
                }
            };
            db.InventoryVouchers.Add(voucher);
            await db.SaveChangesAsync();

            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");
            var result = await controller.Approve(voucher.VoucherId);
            Assert.IsType<RedirectToActionResult>(result);

            // Verify desk increased from 20 to 25
            var deskLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(25, deskLedger.Quantity);

            // Verify chair increased from 50 to 60
            var chairLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(60, chairLedger.Quantity);

            // Verify COMBO-OFFICE-01 has NO static stock ledger
            var comboLedger = await db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == "COMBO-OFFICE-01" && l.WarehouseId == "WH-HN");
            Assert.Null(comboLedger);
        }
    }
}
