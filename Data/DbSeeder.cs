using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<Role>>();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            await db.Database.MigrateAsync();

            var roles = new[] { "Admin", "Director", "WarehouseManager", "Accountant", "QC", "WarehouseKeeper", "Staff" };
            foreach (var r in roles)
            {
                if (!await roleManager.RoleExistsAsync(r))
                {
                    await roleManager.CreateAsync(new Role { Name = r });
                }
            }

            var adminEmail = "admin@wms.com";
            var admin = await userManager.FindByEmailAsync(adminEmail);
            if (admin == null)
            {
                admin = new User
                {
                    UserName = "admin",
                    Email = adminEmail,
                    EmailConfirmed = true,
                    FullName = "System Administrator"
                };
                var createRes = await userManager.CreateAsync(admin, "Admin@123456");
                if (createRes.Succeeded)
                {
                    await userManager.AddToRoleAsync(admin, "Admin");
                }
            }

            if (!await db.Warehouses.AnyAsync())
            {
                db.Warehouses.AddRange(
                    new Warehouse { WarehouseId = "WH-GLOBAL", WarehouseName = "Kho Tổng", WarehouseType = "MAIN", MaxCapacityCbm = 1000m, IsActive = true },
                    new Warehouse { WarehouseId = "WH-DAMAGED", WarehouseName = "Kho Hàng Lỗi", WarehouseType = "DAMAGED", MaxCapacityCbm = 100m, IsActive = true }
                );
            }

            if (!await db.Suppliers.AnyAsync())
            {
                db.Suppliers.AddRange(
                    new Supplier { SupplierId = "SUP-1", TaxCode = "TAX001", Name = "NCC Nội Thất 1", Address = "Hanoi", Email = "sup1@example.local", Phone = "0900000001", IsActive = true },
                    new Supplier { SupplierId = "SUP-2", TaxCode = "TAX002", Name = "NCC Nội Thất 2", Address = "HCMC", Email = "sup2@example.local", Phone = "0900000002", IsActive = true }
                );
            }

            if (!await db.ProductModels.AnyAsync())
            {
                db.ProductModels.AddRange(
                    new ProductModel { ProductModelId = "PM-DESK", ProductName = "Bàn làm việc", Category = "Office", Unit = "pcs", MinStock = 5, IsActive = true },
                    new ProductModel { ProductModelId = "PM-CHAIR", ProductName = "Ghế xoay", Category = "Office", Unit = "pcs", MinStock = 10, IsActive = true },
                    new ProductModel { ProductModelId = "PM-SOFA", ProductName = "Sofa 3 chỗ", Category = "Living", Unit = "set", MinStock = 2, IsActive = true }
                );
            }

            if (!await db.SkuVariants.AnyAsync())
            {
                db.SkuVariants.AddRange(
                    new SkuVariant { SkuId = "SKU-DESK-BR", ProductModelId = "PM-DESK", Color = "Brown", Material = "Wood", LengthMm = 1200, WidthMm = 600, HeightMm = 750, Cbm = ComputeCbm(1200,600,750), UnitPrice = 2500000m, IsActive = true },
                    new SkuVariant { SkuId = "SKU-CHAIR-BK", ProductModelId = "PM-CHAIR", Color = "Black", Material = "Leather", LengthMm = 600, WidthMm = 600, HeightMm = 1000, Cbm = ComputeCbm(600,600,1000), UnitPrice = 1200000m, IsActive = true },
                    new SkuVariant { SkuId = "SKU-SOFA-GY", ProductModelId = "PM-SOFA", Color = "Gray", Material = "Fabric", LengthMm = 2000, WidthMm = 900, HeightMm = 800, Cbm = ComputeCbm(2000,900,800), UnitPrice = 8000000m, IsActive = true }
                );
            }

            await db.SaveChangesAsync();

            if (!await db.ComboProducts.AnyAsync())
            {
                var combo = new ComboProduct { ComboId = "COMBO-SET-1", ComboName = "Set Văn Phòng" };
                db.ComboProducts.Add(combo);
                await db.SaveChangesAsync();

                db.BomComponents.AddRange(
                    new BomComponent { ComboId = combo.ComboId, SkuId = "SKU-DESK-BR", QuantityPerSet = 1 },
                    new BomComponent { ComboId = combo.ComboId, SkuId = "SKU-CHAIR-BK", QuantityPerSet = 1 }
                );
            }

            if (!await db.StockLedgers.AnyAsync())
            {
                db.StockLedgers.AddRange(
                    new StockLedger { SkuId = "SKU-DESK-BR", WarehouseId = "WH-GLOBAL", Status = "GOOD", Quantity = 10 },
                    new StockLedger { SkuId = "SKU-CHAIR-BK", WarehouseId = "WH-GLOBAL", Status = "GOOD", Quantity = 20 },
                    new StockLedger { SkuId = "SKU-SOFA-GY", WarehouseId = "WH-GLOBAL", Status = "GOOD", Quantity = 3 },
                    new StockLedger { SkuId = "SKU-CHAIR-BK", WarehouseId = "WH-DAMAGED", Status = "DAMAGED", Quantity = 1 }
                );

                db.StockMovements.AddRange(
                    new StockMovement { SkuId = "SKU-DESK-BR", WarehouseId = "WH-GLOBAL", StockStatus = "GOOD", QuantityDelta = 10, MovementType = "INITIAL", ReferenceType = "SEED", ReferenceId = "SEED-1", PerformedBy = "U-ADMIN", CreatedAt = DateTime.UtcNow },
                    new StockMovement { SkuId = "SKU-CHAIR-BK", WarehouseId = "WH-GLOBAL", StockStatus = "GOOD", QuantityDelta = 20, MovementType = "INITIAL", ReferenceType = "SEED", ReferenceId = "SEED-1", PerformedBy = "U-ADMIN", CreatedAt = DateTime.UtcNow },
                    new StockMovement { SkuId = "SKU-SOFA-GY", WarehouseId = "WH-GLOBAL", StockStatus = "GOOD", QuantityDelta = 3, MovementType = "INITIAL", ReferenceType = "SEED", ReferenceId = "SEED-1", PerformedBy = "U-ADMIN", CreatedAt = DateTime.UtcNow },
                    new StockMovement { SkuId = "SKU-CHAIR-BK", WarehouseId = "WH-DAMAGED", StockStatus = "DAMAGED", QuantityDelta = 1, MovementType = "INITIAL", ReferenceType = "SEED", ReferenceId = "SEED-1", PerformedBy = "U-ADMIN", CreatedAt = DateTime.UtcNow }
                );
            }

            await db.SaveChangesAsync();
        }

        private static decimal ComputeCbm(int l, int w, int h)
        {
            var cbm = (decimal)l * (decimal)w * (decimal)h / 1000000000m;
            return Math.Round(cbm, 6);
        }
    }
}
