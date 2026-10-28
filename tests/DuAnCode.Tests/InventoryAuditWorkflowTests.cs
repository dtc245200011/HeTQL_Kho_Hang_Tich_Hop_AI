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
    public class InventoryAuditWorkflowTests
    {
        [Fact]
        public async Task Audit_Approval_Adjusts_Stock_And_Creates_Adjustment_Voucher()
        {
            // BR-06: Kiểm kê phát hiện chênh lệch -> Cập nhật tồn & tạo phiếu điều chỉnh
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var controller = new InventoryAuditController(db, repo);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            // In WH-HN: SKU-DESK-01 has 20 in system.
            // Physical count: found only 18 (shortage of 2).
            var audit = new InventoryAudit
            {
                AuditNumber = "KK-Q4-2026",
                WarehouseId = "WH-HN",
                Date = DateTime.UtcNow,
                Status = "DRAFT",
                Lines = new List<InventoryAuditLine>
                {
                    new InventoryAuditLine
                    {
                        SkuId = "SKU-DESK-01",
                        BatchNumber = "",
                        SystemQuantity = 20,
                        ActualQuantity = 18 // Discrepancy: -2
                    }
                }
            };
            db.InventoryAudits.Add(audit);
            await db.SaveChangesAsync();

            var result = await controller.Approve(audit.AuditId);
            Assert.IsType<RedirectToActionResult>(result);

            // Audit should be approved
            var approvedAudit = await db.InventoryAudits.FindAsync(audit.AuditId);
            Assert.Equal("APPROVED", approvedAudit!.Status);

            // Stock ledger in WH-HN should now be 18
            var ledger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-DESK-01" && l.WarehouseId == "WH-HN");
            Assert.Equal(18, ledger.Quantity);

            // Stock movement AUDIT_ADJUSTMENT logged with delta -2
            var movement = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == audit.AuditId && m.MovementType == "AUDIT_ADJUSTMENT");
            Assert.NotNull(movement);
            Assert.Equal(-2, movement.QuantityDelta);

            // Automated ADJUSTMENT voucher must have been generated
            var adjVoucher = await db.InventoryVouchers.FirstOrDefaultAsync(v => v.Type == "ADJUSTMENT" && v.ReferenceDocument == audit.AuditNumber);
            Assert.NotNull(adjVoucher);
            Assert.Equal("APPROVED", adjVoucher.Status);
            Assert.True(adjVoucher.IsLocked);
        }

        [Fact]
        public async Task Audit_Approval_Adjusts_Damaged_Stock()
        {
            // Inventory Audit with StockStatus = DAMAGED should adjust DAMAGED stock ledger and log movement with StockStatus = DAMAGED
            using var db = TestHelper.CreateInMemoryDbContext();
            var repo = new InventoryRepository(db);
            var controller = new InventoryAuditController(db, repo);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            // Seed existing DAMAGED ledger for SKU-CHAIR-01 in WH-HN (5 units)
            db.StockLedgers.Add(new StockLedger
            {
                SkuId = "SKU-CHAIR-01",
                WarehouseId = "WH-HN",
                Status = "DAMAGED",
                Quantity = 5,
                BatchNumber = ""
            });
            await db.SaveChangesAsync();

            // Count reveals 3 damaged chairs (2 repaired or discarded earlier)
            var audit = new InventoryAudit
            {
                AuditNumber = "KK-DAMAGED-01",
                WarehouseId = "WH-HN",
                Date = DateTime.UtcNow,
                Status = "DRAFT",
                Lines = new List<InventoryAuditLine>
                {
                    new InventoryAuditLine
                    {
                        SkuId = "SKU-CHAIR-01",
                        BatchNumber = "",
                        StockStatus = "DAMAGED",
                        SystemQuantity = 5,
                        ActualQuantity = 3
                    }
                }
            };
            db.InventoryAudits.Add(audit);
            await db.SaveChangesAsync();

            var result = await controller.Approve(audit.AuditId);
            Assert.IsType<RedirectToActionResult>(result);

            // Damaged ledger should now be 3
            var damagedLedger = await db.StockLedgers.FirstAsync(l => l.SkuId == "SKU-CHAIR-01" && l.WarehouseId == "WH-HN" && l.Status == "DAMAGED");
            Assert.Equal(3, damagedLedger.Quantity);

            // Stock movement must reflect DAMAGED status with delta -2
            var movement = await db.StockMovements.FirstOrDefaultAsync(m => m.ReferenceId == audit.AuditId && m.StockStatus == "DAMAGED");
            Assert.NotNull(movement);
            Assert.Equal(-2, movement.QuantityDelta);
        }
    }
}
