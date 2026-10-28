using DuAnCode.Web.Controllers;
using DuAnCode.Web.Models;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace DuAnCode.Tests
{
    public class ReportAndReplenishmentTests
    {
        [Fact]
        public async Task Report_Calculates_Beginning_In_Out_Ending_Correctly()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var controller = new ReportController(db);
            TestHelper.SetUserContext(controller, "admin", "Admin");

            // Seed stock movements for SKU-DESK-01 in WH-HN
            // Current Ledger quantity in WH-HN for SKU-DESK-01 is 20.
            var baseTime = DateTime.UtcNow.Date;
            db.StockMovements.AddRange(
                new StockMovement
                {
                    MovementId = 101,
                    SkuId = "SKU-DESK-01",
                    WarehouseId = "WH-HN",
                    MovementType = "INBOUND",
                    StockStatus = "GOOD",
                    QuantityDelta = 10,
                    CreatedAt = baseTime.AddHours(2)
                },
                new StockMovement
                {
                    MovementId = 102,
                    SkuId = "SKU-DESK-01",
                    WarehouseId = "WH-HN",
                    MovementType = "OUTBOUND",
                    StockStatus = "GOOD",
                    QuantityDelta = -4,
                    CreatedAt = baseTime.AddHours(4)
                }
            );
            await db.SaveChangesAsync();

            // Run report for today
            var result = await controller.Index(baseTime, baseTime.AddDays(1), "WH-HN") as ViewResult;
            Assert.NotNull(result);

            var model = result.Model as ReportViewModel;
            Assert.NotNull(model);

            var deskRow = model.ReportRows.FirstOrDefault(r => r.SkuId == "SKU-DESK-01");
            Assert.NotNull(deskRow);
            Assert.Equal(10, deskRow.TotalIn);
            Assert.Equal(4, deskRow.TotalOut);
            Assert.Equal(20, deskRow.Ending);
            Assert.Equal(20, deskRow.EndingGood);
            Assert.Equal(0, deskRow.EndingDamaged);
            // Beginning = Ending - TotalIn + TotalOut = 20 - 10 + 4 = 14
            Assert.Equal(14, deskRow.Beginning);
        }

        [Fact]
        public async Task Report_ExportExcel_Returns_Csv_With_Utf8Bom()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var controller = new ReportController(db);
            TestHelper.SetUserContext(controller, "admin", "Admin");

            var result = await controller.ExportExcel(null, null, "WH-HN") as FileContentResult;
            Assert.NotNull(result);
            Assert.Equal("text/csv; charset=utf-8", result.ContentType);
            Assert.StartsWith("BaoCao_NXT_", result.FileDownloadName);
            Assert.True(result.FileContents.Length > 0);
            // Preamble UTF-8 BOM: EF BB BF
            Assert.Equal(0xEF, result.FileContents[0]);
            Assert.Equal(0xBB, result.FileContents[1]);
            Assert.Equal(0xBF, result.FileContents[2]);
        }

        [Fact]
        public async Task Replenishment_Approve_Generates_Draft_Inbound_Voucher()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var controller = new ReplenishmentController(db);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            var payload = new { sku = "SKU-CHAIR-01", suggested = 25 };
            var suggestion = new AiSuggestion
            {
                SuggestionId = Guid.NewGuid().ToString(),
                SuggestionType = "REPLENISHMENT",
                SkuId = "SKU-CHAIR-01",
                PayloadJson = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
                Status = "PENDING_REVIEW"
            };
            db.AiSuggestions.Add(suggestion);
            await db.SaveChangesAsync();

            // Approve suggestion
            var res = await controller.Approve(suggestion.SuggestionId) as RedirectToActionResult;
            Assert.NotNull(res);
            Assert.Equal("Index", res.ActionName);

            // Verify suggestion updated
            var updatedSug = await db.AiSuggestions.FindAsync(suggestion.SuggestionId);
            Assert.NotNull(updatedSug);
            Assert.Equal("APPROVED", updatedSug.Status);
            Assert.Equal("quanly1", updatedSug.ReviewedBy);

            // Verify Inbound voucher generated
            var voucher = await db.InventoryVouchers
                .Include(v => v.Lines)
                .FirstOrDefaultAsync(v => v.Reason == "Đề xuất tự động từ AI");
            Assert.NotNull(voucher);
            Assert.Equal("INBOUND", voucher.Type);
            Assert.Equal("DRAFT", voucher.Status);
            Assert.Single(voucher.Lines);
            Assert.Equal("SKU-CHAIR-01", voucher.Lines.First().SkuId);
            Assert.Equal("SKU-CHAIR-01", voucher.Lines.First().SkuCode);
            Assert.Equal(25, voucher.Lines.First().Quantity);
            Assert.False(string.IsNullOrWhiteSpace(voucher.Counterparty));
            Assert.False(string.IsNullOrWhiteSpace(voucher.WarehouseId));
        }

        [Fact]
        public async Task Replenishment_Approve_Populates_Full_Details_With_Price_And_Supplier()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            db.Suppliers.Add(new Supplier { SupplierId = "SUP-TEST", Name = "Công ty Gỗ Đức Hậu", Address = "Hà Nội", TaxCode = "01010101", IsActive = true });
            await db.SaveChangesAsync();

            var controller = new ReplenishmentController(db);
            TestHelper.SetUserContext(controller, "admin", "Admin");

            var payload = new { sku = "SKU-DESK-01", productName = "Bàn làm việc Giám đốc", currentStock = 2, minStock = 5, suggested = 10, reason = "Tồn kho dưới ngưỡng an toàn" };
            var suggestion = new AiSuggestion
            {
                SuggestionId = Guid.NewGuid().ToString(),
                SuggestionType = "LOW_STOCK",
                SkuId = "SKU-DESK-01",
                PayloadJson = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
                Status = "PENDING_REVIEW"
            };
            db.AiSuggestions.Add(suggestion);
            await db.SaveChangesAsync();

            var res = await controller.Approve(suggestion.SuggestionId) as RedirectToActionResult;
            Assert.NotNull(res);

            var voucher = await db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.Lines.Any(l => l.SkuId == "SKU-DESK-01"));
            Assert.NotNull(voucher);
            Assert.Equal("WH-HN", voucher.WarehouseId); // MAIN warehouse in test seed
            Assert.Equal("Công ty Gỗ Đức Hậu", voucher.Counterparty);
            Assert.Equal("Hà Nội", voucher.Address);
            Assert.Equal(50000000m, voucher.TotalAmount); // 10 * 5,000,000
            Assert.Contains("Tồn kho thấp dưới định mức", voucher.Reason);

            var line = voucher.Lines.First();
            Assert.Equal("SKU-DESK-01", line.SkuCode);
            Assert.Equal("Bàn làm việc Giám đốc", line.ProductName);
            Assert.Equal("Cái", line.Unit);
            Assert.Equal(10, line.Quantity);
            Assert.Equal(5000000m, line.UnitPrice);
            Assert.Equal(50000000m, line.LineTotal);
            Assert.StartsWith("LÔ-AI-", line.BatchNumber);
        }

        [Fact]
        public async Task Replenishment_Reject_Marks_Suggestion_Rejected()
        {
            using var db = TestHelper.CreateInMemoryDbContext();
            var controller = new ReplenishmentController(db);
            TestHelper.SetUserContext(controller, "quanly1", "WarehouseManager");

            var suggestion = new AiSuggestion
            {
                SuggestionId = Guid.NewGuid().ToString(),
                SuggestionType = "REPLENISHMENT",
                SkuId = "SKU-DESK-01",
                PayloadJson = "{\"sku\":\"SKU-DESK-01\",\"suggested\":10}",
                CreatedAt = DateTime.UtcNow,
                Status = "PENDING_REVIEW"
            };
            db.AiSuggestions.Add(suggestion);
            await db.SaveChangesAsync();

            var res = await controller.Reject(suggestion.SuggestionId) as RedirectToActionResult;
            Assert.NotNull(res);

            var updatedSug = await db.AiSuggestions.FindAsync(suggestion.SuggestionId);
            Assert.NotNull(updatedSug);
            Assert.Equal("REJECTED", updatedSug.Status);
        }
    }
}
