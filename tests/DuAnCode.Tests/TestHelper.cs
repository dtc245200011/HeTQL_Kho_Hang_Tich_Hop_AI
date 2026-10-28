using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace DuAnCode.Tests
{
    public static class TestHelper
    {
        public static ApplicationDbContext CreateInMemoryDbContext(string dbName = "")
        {
            if (string.IsNullOrEmpty(dbName))
                dbName = Guid.NewGuid().ToString();

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;

            var db = new ApplicationDbContext(options);
            SeedTestData(db);
            return db;
        }

        public static void SeedTestData(ApplicationDbContext db)
        {
            // Warehouses
            db.Warehouses.AddRange(
                new Warehouse { WarehouseId = "WH-HN", WarehouseName = "Kho Hà Nội", WarehouseType = "MAIN", MaxCapacityCbm = 1000m, IsActive = true },
                new Warehouse { WarehouseId = "WH-HCM", WarehouseName = "Kho Hồ Chí Minh", WarehouseType = "BRANCH", MaxCapacityCbm = 800m, IsActive = true },
                new Warehouse { WarehouseId = "WH-DAMAGED", WarehouseName = "Kho Lỗi", WarehouseType = "DAMAGED", MaxCapacityCbm = 100m, IsActive = true }
            );

            // Product Models
            db.ProductModels.AddRange(
                new ProductModel { ProductModelId = "PM-DESK", ProductName = "Bàn làm việc Giám đốc", Category = "Bàn", Unit = "Cái", MinStock = 5, IsActive = true },
                new ProductModel { ProductModelId = "PM-CHAIR", ProductName = "Ghế Công Thái Học", Category = "Ghế", Unit = "Cái", MinStock = 10, IsActive = true },
                new ProductModel { ProductModelId = "PM-SOFA", ProductName = "Sofa Da Cao Cấp", Category = "Sofa", Unit = "Bộ", MinStock = 3, IsActive = true }
            );

            // Sku Variants
            db.SkuVariants.AddRange(
                new SkuVariant { SkuId = "SKU-DESK-01", ProductModelId = "PM-DESK", Color = "Nâu", Material = "Gỗ Sồi", LengthMm = 1600, WidthMm = 800, HeightMm = 750, Cbm = 0.96m, UnitPrice = 5000000m, IsActive = true },
                new SkuVariant { SkuId = "SKU-CHAIR-01", ProductModelId = "PM-CHAIR", Color = "Đen", Material = "Lưới", LengthMm = 650, WidthMm = 650, HeightMm = 1100, Cbm = 0.46475m, UnitPrice = 2500000m, IsActive = true },
                new SkuVariant { SkuId = "SKU-SOFA-01", ProductModelId = "PM-SOFA", Color = "Xám", Material = "Da Ý", LengthMm = 2200, WidthMm = 950, HeightMm = 850, Cbm = 1.7765m, UnitPrice = 25000000m, IsActive = true }
            );

            // Combo Products & BOM
            var combo = new ComboProduct { ComboId = "COMBO-OFFICE-01", ComboName = "Combo Bàn Ghế Văn Phòng" };
            db.ComboProducts.Add(combo);
            db.BomComponents.AddRange(
                new BomComponent { ComboId = "COMBO-OFFICE-01", SkuId = "SKU-DESK-01", QuantityPerSet = 1 },
                new BomComponent { ComboId = "COMBO-OFFICE-01", SkuId = "SKU-CHAIR-01", QuantityPerSet = 2 }
            );

            // Initial Stock Ledgers
            db.StockLedgers.AddRange(
                new StockLedger { SkuId = "SKU-DESK-01", WarehouseId = "WH-HN", Status = "GOOD", Quantity = 20, BatchNumber = "" },
                new StockLedger { SkuId = "SKU-CHAIR-01", WarehouseId = "WH-HN", Status = "GOOD", Quantity = 50, BatchNumber = "" },
                new StockLedger { SkuId = "SKU-SOFA-01", WarehouseId = "WH-HN", Status = "GOOD", Quantity = 2, BatchNumber = "" },
                new StockLedger { SkuId = "SKU-CHAIR-01", WarehouseId = "WH-HCM", Status = "GOOD", Quantity = 15, BatchNumber = "" }
            );

            // System Config
            db.SystemConfigs.Add(new SystemConfig
            {
                Id = 1,
                DefaultLanguage = "vi-VN",
                CurrencyFormat = "VND",
                LowStockAlertThreshold = 5,
                CapacityAlertPercent = 90,
                InboundPrefix = "PN-",
                OutboundPrefix = "PX-",
                MaintenanceMode = false,
                StopInbound = false,
                StopOutbound = false
            });

            db.SaveChanges();
        }

        public static void SetUserContext(Controller controller, string userName, params string[] roles)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, userName),
                new Claim(ClaimTypes.NameIdentifier, userName)
            };
            foreach (var r in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, r));
            }

            var identity = new ClaimsIdentity(claims, "TestAuth");
            var principal = new ClaimsPrincipal(identity);

            var httpContext = new DefaultHttpContext { User = principal };
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };
            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        }
    }
}
