using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;

namespace DuAnCode.Web.Controllers
{
    [Authorize(Roles = "Admin,Director")]
    public class AdminController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly UserManager<User> _userManager;
        private readonly RoleManager<Role> _roleManager;
        private readonly SignInManager<User> _signInManager;

        public AdminController(ApplicationDbContext db, UserManager<User> userManager, RoleManager<Role> roleManager, SignInManager<User> signInManager)
        {
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _signInManager = signInManager;
        }

        // Ensure minimal admin role and admin user linkage
        private async Task SeedAdminRoleAsync()
        {
            // Clean duplicates before ensuring admin exists
            await CleanupDuplicateAdminUsers();
            // Ensure roles exist
            var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole == null)
            {
                adminRole = new Role { Id = Guid.NewGuid().ToString(), Name = "Admin", NormalizedName = "ADMIN" };
                await _db.Roles.AddAsync(adminRole);
                await _db.SaveChangesAsync();
            }

            // Ensure admin user exists using UserManager (avoid direct _db.Add)
            var adminUser = await _userManager.FindByNameAsync("admin");
            if (adminUser == null)
            {
                adminUser = new User { UserName = "admin", Email = "admin@example.com", EmailConfirmed = true, FullName = "System Administrator" };
                var createRes = await _userManager.CreateAsync(adminUser, "ChangeMe123!");
                if (!createRes.Succeeded)
                {
                    // if create failed, attempt to fetch existing by email or username
                    adminUser = await _userManager.FindByNameAsync("admin") ?? await _userManager.FindByEmailAsync("admin@example.com");
                }
            }

            // Ensure user-role link exists via UserManager
            if (adminUser != null && !await _userManager.IsInRoleAsync(adminUser, "Admin"))
            {
                await _userManager.AddToRoleAsync(adminUser, "Admin");
            }
        }

        // Cleanup duplicate admin users: keep first, delete others
        private async Task CleanupDuplicateAdminUsers()
        {
            try
            {
                var admins = await _db.Users.Where(u => u.UserName == "admin").OrderBy(u => u.Id).ToListAsync();
                if (admins.Count <= 1) return;
                var keeper = admins.First();
                foreach (var dupe in admins.Skip(1))
                {
                    try
                    {
                        // remove roles for the duplicate user first
                        var u = await _userManager.FindByIdAsync(dupe.Id);
                        if (u != null)
                        {
                            var roles = await _userManager.GetRolesAsync(u);
                            if (roles.Any()) await _userManager.RemoveFromRolesAsync(u, roles);
                            await _userManager.DeleteAsync(u);
                        }
                        else
                        {
                            // fallback: delete via EF if user not found by UserManager
                            _db.Users.Remove(dupe);
                        }
                    }
                    catch { /* ignore individual delete failures */ }
                }
                await _db.SaveChangesAsync();
            }
            catch { }
        }

        // Helper to import entities from a JsonElement (used for rollback re-import)
        private async Task ImportEntitiesFromDocumentAsync(System.Text.Json.JsonElement root)
        {
            // Roles
            if (root.TryGetProperty("Roles", out var rolesEl))
            {
                try
                {
                    var roles = System.Text.Json.JsonSerializer.Deserialize<List<Role>>(rolesEl.GetRawText());
                    if (roles != null && roles.Any()) { await _db.Roles.AddRangeAsync(roles); await _db.SaveChangesAsync(); }
                }
                catch { }
            }

            // Users
            if (root.TryGetProperty("Users", out var usersEl))
            {
                try
                {
                    var users = System.Text.Json.JsonSerializer.Deserialize<List<User>>(usersEl.GetRawText());
                    if (users != null && users.Any()) { await _db.Users.AddRangeAsync(users); await _db.SaveChangesAsync(); }
                }
                catch { }
            }

            // UserRoles (junction)
            if (root.TryGetProperty("UserRoles", out var urEl))
            {
                try
                {
                    var urs = System.Text.Json.JsonSerializer.Deserialize<List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>>(urEl.GetRawText());
                    if (urs != null && urs.Any()) { await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AddRangeAsync(urs); await _db.SaveChangesAsync(); }
                }
                catch { }
            }

            // Warehouses
            if (root.TryGetProperty("Warehouses", out var whsEl))
            {
                try { var whs = System.Text.Json.JsonSerializer.Deserialize<List<Warehouse>>(whsEl.GetRawText()); if (whs != null && whs.Any()) { await _db.Warehouses.AddRangeAsync(whs); await _db.SaveChangesAsync(); } } catch { }
            }

            // ProductModels
            if (root.TryGetProperty("ProductModels", out var pmEl))
            {
                try { var pms = System.Text.Json.JsonSerializer.Deserialize<List<ProductModel>>(pmEl.GetRawText()); if (pms != null && pms.Any()) { await _db.ProductModels.AddRangeAsync(pms); await _db.SaveChangesAsync(); } } catch { }
            }

            // SkuVariants
            if (root.TryGetProperty("SkuVariants", out var skEl))
            {
                try { var sks = System.Text.Json.JsonSerializer.Deserialize<List<SkuVariant>>(skEl.GetRawText()); if (sks != null && sks.Any()) { await _db.SkuVariants.AddRangeAsync(sks); await _db.SaveChangesAsync(); } } catch { }
            }

            // StockLedgers
            if (root.TryGetProperty("StockLedgers", out var slEl))
            {
                try { var sls = System.Text.Json.JsonSerializer.Deserialize<List<StockLedger>>(slEl.GetRawText()); if (sls != null && sls.Any()) { await _db.StockLedgers.AddRangeAsync(sls); await _db.SaveChangesAsync(); } } catch { }
            }

            // StockMovements
            if (root.TryGetProperty("StockMovements", out var smEl))
            {
                try { var sms = System.Text.Json.JsonSerializer.Deserialize<List<StockMovement>>(smEl.GetRawText()); if (sms != null && sms.Any()) { await _db.StockMovements.AddRangeAsync(sms); await _db.SaveChangesAsync(); } } catch { }
            }

            // InventoryVouchers
            if (root.TryGetProperty("InventoryVouchers", out var ivEl))
            {
                try { var ivs = System.Text.Json.JsonSerializer.Deserialize<List<InventoryVoucher>>(ivEl.GetRawText()); if (ivs != null && ivs.Any()) { await _db.InventoryVouchers.AddRangeAsync(ivs); await _db.SaveChangesAsync(); } } catch { }
            }

            // VoucherLines
            if (root.TryGetProperty("VoucherLines", out var vlEl))
            {
                try { var vls = System.Text.Json.JsonSerializer.Deserialize<List<VoucherLine>>(vlEl.GetRawText()); if (vls != null && vls.Any()) { await _db.VoucherLines.AddRangeAsync(vls); await _db.SaveChangesAsync(); } } catch { }
            }

            // AiSuggestions
            if (root.TryGetProperty("AiSuggestions", out var aiEl))
            {
                try { var ais = System.Text.Json.JsonSerializer.Deserialize<List<AiSuggestion>>(aiEl.GetRawText()); if (ais != null && ais.Any()) { await _db.AiSuggestions.AddRangeAsync(ais); await _db.SaveChangesAsync(); } } catch { }
            }

            // AuditLogs
            if (root.TryGetProperty("AuditLogs", out var alEl))
            {
                try { var als = System.Text.Json.JsonSerializer.Deserialize<List<AuditLog>>(alEl.GetRawText()); if (als != null && als.Any()) { await _db.AuditLogs.AddRangeAsync(als); await _db.SaveChangesAsync(); } } catch { }
            }
        }

        public async Task<IActionResult> Index()
        {
            var totalUsers = await _db.Users.CountAsync();
            var totalSkus = await _db.SkuVariants.CountAsync();
            var totalWarehouses = await _db.Warehouses.CountAsync();

            ViewBag.TotalUsers = totalUsers;
            ViewBag.TotalSkus = totalSkus;
            ViewBag.TotalWarehouses = totalWarehouses;

            // basic DB connectivity check
            bool dbOk = true;
            try { await _db.Database.CanConnectAsync(); } catch { dbOk = false; }
            ViewBag.DbOk = dbOk;

            return View();
        }

        public async Task<IActionResult> Users()
        {
            // Use DTO projection to avoid potential EF circular serialization and to ensure up-to-date user manager data
            var users = await _db.Users.AsNoTracking().Select(u => new User { Id = u.Id, UserName = u.UserName, Email = u.Email, LockoutEnd = u.LockoutEnd }).ToListAsync();
            var model = new List<AdminUserViewModel>();
            foreach (var u in users)
            {
                var roles = await _userManager.GetRolesAsync(u);
                model.Add(new AdminUserViewModel
                {
                    Id = u.Id,
                    UserName = u.UserName ?? string.Empty,
                    Email = u.Email ?? string.Empty,
                    Roles = string.Join(", ", roles),
                    IsLockedOut = u.LockoutEnd != null && u.LockoutEnd > DateTimeOffset.UtcNow
                });
            }
            // also supply available roles
            ViewBag.AvailableRoles = await _roleManager.Roles.Select(r => r.Name).ToListAsync();
            ViewBag.TotalUsers = await _db.Users.CountAsync();
            ViewBag.CurrentUserId = _userManager.GetUserId(User);
            // find current admin id if any
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            ViewBag.AdminUserId = admins.FirstOrDefault()?.Id;
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUserAjax(string userName, string fullName, string email, string password, string role, bool confirm = false)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                return Json(new { success = false, message = "Username và mật khẩu là bắt buộc" });
            }

            // enforce single admin rule
            if (!string.IsNullOrWhiteSpace(role) && role == "Admin")
            {
                var existingAdmins = await _userManager.GetUsersInRoleAsync("Admin");
                var existing = existingAdmins.FirstOrDefault();
                if (existing != null)
                {
                    if (!confirm)
                    {
                        return Json(new { success = false, requiresConfirmation = true, message = "Hệ thống hiện đã có 1 Admin. Xác nhận để thay thế (cũ sẽ bị xóa).", oldAdminId = existing.Id, oldAdminUser = existing.UserName });
                    }
                }
            }

            var user = new User { UserName = userName, Email = email ?? string.Empty, FullName = fullName ?? string.Empty };
            var res = await _userManager.CreateAsync(user, password);
            if (!res.Succeeded)
            {
                return Json(new { success = false, message = string.Join("; ", res.Errors.Select(e => e.Description)) });
            }

            if (!string.IsNullOrWhiteSpace(role) && await _roleManager.RoleExistsAsync(role))
            {
                await _userManager.AddToRoleAsync(user, role);

                if (role == "Admin")
                {
                    // if there was an old admin and confirm=true, delete old admin
                    var existingAdmins = await _userManager.GetUsersInRoleAsync("Admin");
                    foreach (var a in existingAdmins)
                    {
                        if (a.Id != user.Id)
                        {
                            await _userManager.DeleteAsync(a);
                            // if deleted user is current user, signal logout
                            if (_userManager.GetUserId(User) == a.Id)
                            {
                                return Json(new { success = true, logout = true, redirect = Url.Action("Logout", "Account") });
                            }
                        }
                    }
                }
            }

            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRoleAjax(string userId, string role, bool confirm = false)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found" });

            var totalUsers = await _db.Users.CountAsync();
            if (totalUsers <= 1)
            {
                // ensure sole user is Admin
                if (!await _userManager.IsInRoleAsync(user, "Admin"))
                {
                    var resAdd = await _userManager.AddToRoleAsync(user, "Admin");
                    if (!resAdd.Succeeded) return Json(new { success = false, message = "Cannot promote sole user to Admin" });
                }
                return Json(new { success = false, message = "Operation blocked: only one user exists; role must be Admin" });
            }

            // if promoting to Admin, and existing admin exists
            if (!string.IsNullOrWhiteSpace(role) && role == "Admin")
            {
                var existingAdmins = await _userManager.GetUsersInRoleAsync("Admin");
                var existing = existingAdmins.FirstOrDefault();
                if (existing != null && existing.Id != user.Id)
                {
                    if (!confirm)
                    {
                        return Json(new { success = false, requiresConfirmation = true, message = "System already has an Admin. Confirm to replace and delete old Admin.", oldAdminId = existing.Id, oldAdminUser = existing.UserName });
                    }

                    // confirmed: delete old admin
                    var delRes = await _userManager.DeleteAsync(existing);
                    if (!delRes.Succeeded) return Json(new { success = false, message = "Failed to remove old admin" });
                    if (_userManager.GetUserId(User) == existing.Id)
                    {
                        // current user deleted -> force logout
                        await _userManager.AddToRoleAsync(user, "Admin");
                        return Json(new { success = true, logout = true, redirect = Url.Action("Logout", "Account") });
                    }
                }
            }

            // remove existing roles and add new one
            var current = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, current);
            if (!string.IsNullOrWhiteSpace(role) && await _roleManager.RoleExistsAsync(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteUserAjax(string userId)
        {
            var totalUsers = await _db.Users.CountAsync();
            if (totalUsers <= 1) return Json(new { success = false, message = "Cannot delete the only user in system" });

            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return Json(new { success = false, message = "User not found" });

            // New rule: Never allow deleting an account that currently has Admin role
            if (await _userManager.IsInRoleAsync(user, "Admin"))
            {
                // record a friendly message for full-page flows and also return JSON for AJAX
                TempData["Error"] = "Không thể xóa tài khoản Quản trị viên (Admin) để đảm bảo an toàn hệ thống!";
                return Json(new { success = false, message = "Cannot delete an Admin account." });
            }

            // prevent deleting sole admin if it would leave system without admin (legacy safety)
            var admins = await _userManager.GetUsersInRoleAsync("Admin");
            if (admins.Count == 1 && admins.First().Id == user.Id)
            {
                return Json(new { success = false, message = "Cannot delete the only Admin. Assign another Admin first." });
            }

            var res = await _userManager.DeleteAsync(user);
            if (!res.Succeeded) return Json(new { success = false, message = string.Join("; ", res.Errors.Select(e => e.Description)) });
            return Json(new { success = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string userName, string fullName, string email, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(userName) || string.IsNullOrWhiteSpace(password))
            {
                TempData["Error"] = "Username và mật khẩu là bắt buộc";
                return RedirectToAction("Users");
            }

            var user = new User { UserName = userName, Email = email ?? string.Empty, FullName = fullName ?? string.Empty };
            var res = await _userManager.CreateAsync(user, password);
            if (!res.Succeeded)
            {
                TempData["Error"] = string.Join("; ", res.Errors.Select(e => e.Description));
                return RedirectToAction("Users");
            }
            if (!string.IsNullOrWhiteSpace(role) && await _roleManager.RoleExistsAsync(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }
            TempData["Message"] = "Tạo người dùng thành công.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeRole(string userId, string role)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var current = await _userManager.GetRolesAsync(user);
            await _userManager.RemoveFromRolesAsync(user, current);
            if (!string.IsNullOrWhiteSpace(role) && await _roleManager.RoleExistsAsync(role))
            {
                await _userManager.AddToRoleAsync(user, role);
            }
            TempData["Message"] = "Cập nhật phân quyền thành công.";
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleLock(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            if (await _userManager.IsLockedOutAsync(user))
            {
                await _userManager.SetLockoutEndDateAsync(user, null);
                TempData["Message"] = "Mở khóa tài khoản thành công.";
            }
            else
            {
                await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
                TempData["Message"] = "Khóa tài khoản thành công.";
            }
            return RedirectToAction("Users");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string userId, string newPassword)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var res = await _userManager.ResetPasswordAsync(user, token, newPassword);
            if (!res.Succeeded)
            {
                TempData["Error"] = string.Join("; ", res.Errors.Select(e => e.Description));
            }
            else TempData["Message"] = "Reset mật khẩu thành công.";
            return RedirectToAction("Users");
        }

        [HttpGet]
        public async Task<IActionResult> Settings()
        {
            var viewModel = new SettingsViewModel();

            // Safely load SystemConfig
            try
            {
                viewModel.Config = await _db.SystemConfigs.FirstOrDefaultAsync() ?? new SystemConfig();
            }
            catch
            {
                viewModel.Config = new SystemConfig
                {
                    CompanyName = "Kho Tổng WH-GLOBAL",
                    Address = "123 Đường ABC, Hà Nội",
                    Phone = "0901234567",
                    Email = "admin@duancode.com",
                    AutoBackup = false
                };
            }

            // Safely load warehouses
            try
            {
                viewModel.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            }
            catch
            {
                viewModel.Warehouses = new List<Warehouse>();
            }

            return View(viewModel);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SettingsPost(DuAnCode.Web.Models.SettingsViewModel vm)
        {
            if (vm == null || vm.Config == null) return RedirectToAction("Settings");

            // Ensure the SystemConfigs table exists and has necessary columns (safe-create/alter for SQL Server)
            try
            {
                await _db.Database.ExecuteSqlRawAsync(@"
                    IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemConfigs')
                    BEGIN
                        CREATE TABLE SystemConfigs (
                            Id INT IDENTITY(1,1) PRIMARY KEY,
                            WarehouseName NVARCHAR(255) NULL,
                            CompanyName NVARCHAR(255) NULL,
                            Address NVARCHAR(500) NULL,
                            Phone NVARCHAR(50) NULL,
                            Email NVARCHAR(100) NULL,
                            AutoBackup BIT NOT NULL DEFAULT 0,
                            MaintenanceMode BIT NOT NULL DEFAULT 0,
                            StopInbound BIT NOT NULL DEFAULT 0,
                            StopOutbound BIT NOT NULL DEFAULT 0,
                            StopApi BIT NOT NULL DEFAULT 0,
                            LowStockAlertThreshold INT NOT NULL DEFAULT 10,
                            CapacityAlertPercent INT NOT NULL DEFAULT 90,
                            EnableEmailAlerts BIT NOT NULL DEFAULT 1,
                            InboundPrefix NVARCHAR(50) NOT NULL DEFAULT 'PN-',
                            OutboundPrefix NVARCHAR(50) NOT NULL DEFAULT 'PX-',
                            BarcodeFormat NVARCHAR(50) NOT NULL DEFAULT 'QR',
                            LabelWidth INT NOT NULL DEFAULT 50,
                            LabelHeight INT NOT NULL DEFAULT 50,
                            PrintPriceOnLabel BIT NOT NULL DEFAULT 1,
                            PrintExpiryOnLabel BIT NOT NULL DEFAULT 1,
                            OllamaUrl NVARCHAR(255) NOT NULL DEFAULT 'http://localhost:11434',
                            OllamaModel NVARCHAR(100) NOT NULL DEFAULT 'llama3',
                            AiScanIntervalMinutes INT NOT NULL DEFAULT 30,
                            DefaultLanguage NVARCHAR(50) NOT NULL DEFAULT 'vi-VN',
                            CurrencyFormat NVARCHAR(50) NOT NULL DEFAULT 'VND'
                        );
                    END
                    ELSE
                    BEGIN
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'MaintenanceMode' AND Object_ID = Object_ID(N'SystemConfigs'))
                        BEGIN
                            ALTER TABLE SystemConfigs ADD 
                                MaintenanceMode BIT NOT NULL DEFAULT 0,
                                StopInbound BIT NOT NULL DEFAULT 0,
                                StopOutbound BIT NOT NULL DEFAULT 0,
                                StopApi BIT NOT NULL DEFAULT 0,
                                LowStockAlertThreshold INT NOT NULL DEFAULT 10,
                                CapacityAlertPercent INT NOT NULL DEFAULT 90,
                                EnableEmailAlerts BIT NOT NULL DEFAULT 1;
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'InboundPrefix' AND Object_ID = Object_ID(N'SystemConfigs'))
                        BEGIN
                            ALTER TABLE SystemConfigs ADD 
                                InboundPrefix NVARCHAR(50) NOT NULL DEFAULT 'PN-',
                                OutboundPrefix NVARCHAR(50) NOT NULL DEFAULT 'PX-',
                                BarcodeFormat NVARCHAR(50) NOT NULL DEFAULT 'QR',
                                LabelWidth INT NOT NULL DEFAULT 50,
                                LabelHeight INT NOT NULL DEFAULT 50,
                                PrintPriceOnLabel BIT NOT NULL DEFAULT 1,
                                PrintExpiryOnLabel BIT NOT NULL DEFAULT 1,
                                OllamaUrl NVARCHAR(255) NOT NULL DEFAULT 'http://localhost:11434',
                                OllamaModel NVARCHAR(100) NOT NULL DEFAULT 'llama3',
                                AiScanIntervalMinutes INT NOT NULL DEFAULT 30,
                                DefaultLanguage NVARCHAR(50) NOT NULL DEFAULT 'vi-VN',
                                CurrencyFormat NVARCHAR(50) NOT NULL DEFAULT 'VND';
                        END
                        IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'AiScanIntervalMinutes' AND Object_ID = Object_ID(N'SystemConfigs'))
                        BEGIN
                            ALTER TABLE SystemConfigs ADD 
                                AiScanIntervalMinutes INT NOT NULL DEFAULT 30;
                        END
                    END
                ");
            }
            catch
            {
                // Ignore failures here (e.g., not SQL Server) and proceed with EF path
            }

            try
            {
                var model = vm.Config;

                // Now perform insert or update depending on existing data
                // NOTE: After ensuring table exists, reading FirstOrDefaultAsync should be safe.
                var existing = await _db.SystemConfigs.FirstOrDefaultAsync();
                if (existing == null)
                {
                    if (model.AiScanIntervalMinutes <= 0) model.AiScanIntervalMinutes = 30;
                    _db.SystemConfigs.Add(model);
                }
                else
                {
                    existing.CompanyName = model.CompanyName;
                    existing.Address = model.Address;
                    existing.Phone = model.Phone;
                    existing.AutoBackup = model.AutoBackup;
                    existing.Email = model.Email;
                    
                    // Stop & Alerts fields
                    existing.MaintenanceMode = model.MaintenanceMode;
                    existing.StopInbound = model.StopInbound;
                    existing.StopOutbound = model.StopOutbound;
                    existing.StopApi = model.StopApi;
                    existing.LowStockAlertThreshold = model.LowStockAlertThreshold;
                    existing.CapacityAlertPercent = model.CapacityAlertPercent;
                    existing.EnableEmailAlerts = model.EnableEmailAlerts;

                    // New Fields
                    existing.InboundPrefix = model.InboundPrefix ?? "PN-";
                    existing.OutboundPrefix = model.OutboundPrefix ?? "PX-";
                    existing.BarcodeFormat = model.BarcodeFormat ?? "QR";
                    existing.LabelWidth = model.LabelWidth > 0 ? model.LabelWidth : 50;
                    existing.LabelHeight = model.LabelHeight > 0 ? model.LabelHeight : 50;
                    existing.PrintPriceOnLabel = model.PrintPriceOnLabel;
                    existing.PrintExpiryOnLabel = model.PrintExpiryOnLabel;
                    existing.OllamaUrl = string.IsNullOrWhiteSpace(model.OllamaUrl) ? "http://localhost:11434" : model.OllamaUrl;
                    existing.OllamaModel = string.IsNullOrWhiteSpace(model.OllamaModel) ? "qwen2.5-coder:7b" : model.OllamaModel;
                    existing.AiScanIntervalMinutes = model.AiScanIntervalMinutes > 0 ? model.AiScanIntervalMinutes : 30;
                    existing.DefaultLanguage = model.DefaultLanguage ?? "vi-VN";
                    existing.CurrencyFormat = model.CurrencyFormat ?? "VND";
                }

                await _db.SaveChangesAsync();
                TempData["Success"] = "✔️ Cập nhật cấu hình hệ thống thành công!";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi khi lưu cấu hình: " + ex.Message;
            }

            return RedirectToAction("Settings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddWarehouse(string name, string code, string type, string? location, decimal? capacity)
        {
            // Basic validation
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(code))
            {
                TempData["Error"] = "Tên kho và Mã kho là bắt buộc.";
                return RedirectToAction("Settings");
            }
            if (capacity == null || capacity <= 0)
            {
                TempData["Error"] = "Sức chứa phải lớn hơn 0.";
                return RedirectToAction("Settings");
            }

            // Check duplicates by name or code
            var dup = await _db.Warehouses.AnyAsync(w => w.WarehouseName == name || w.WarehouseId == code);
            if (dup)
            {
                TempData["Error"] = "Tên kho hoặc Mã kho đã tồn tại.";
                return RedirectToAction("Settings");
            }

            var w = new Warehouse
            {
                WarehouseId = code,
                WarehouseName = name,
                WarehouseType = string.IsNullOrWhiteSpace(type) ? "MAIN" : type,
                MaxCapacityCbm = capacity ?? 0,
                Location = location
            };
            _db.Warehouses.Add(w);
            await _db.SaveChangesAsync();
            TempData["Message"] = "Thêm kho thành công.";
            return RedirectToAction("Settings");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteWarehouse(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction("Settings");

            // Business rule: prevent deletion if warehouse contains stock
            var hasStock = await _db.StockLedgers.AnyAsync(i => i.WarehouseId == id && i.Quantity > 0);
            if (hasStock)
            {
                TempData["Error"] = "❌ Không thể xóa kho đang chứa hàng tồn! Vui lòng xuất hoặc chuyển hết hàng sang kho khác trước.";
                return RedirectToAction("Settings");
            }

            var w = await _db.Warehouses.FirstOrDefaultAsync(wb => wb.WarehouseId == id);
            if (w != null)
            {
                _db.Warehouses.Remove(w);
                await _db.SaveChangesAsync();
                TempData["Message"] = "Xóa kho thành công.";
            }
            return RedirectToAction("Settings");
        }

        public async Task<IActionResult> Logs(DateTime? from, DateTime? to, string? q)
        {
            // AuditLog uses PerformedAt and PerformedBy
            var query = _db.AuditLogs.AsNoTracking().AsQueryable();
            if (from.HasValue) query = query.Where(x => x.PerformedAt >= from.Value);
            if (to.HasValue) query = query.Where(x => x.PerformedAt <= to.Value.AddDays(1));
            if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => (x.PerformedBy ?? string.Empty).Contains(q) || (x.Action ?? string.Empty).Contains(q) || (x.BeforeJson ?? string.Empty).Contains(q) || (x.AfterJson ?? string.Empty).Contains(q));
            var logs = await query.OrderByDescending(l => l.PerformedAt).Take(500).ToListAsync();
            return View(logs);
        }

        [HttpGet]
        public IActionResult Backup()
        {
            var backups = new List<dynamic>();
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            foreach (var f in Directory.GetFiles(dir).OrderByDescending(f=>f))
            {
                var fi = new FileInfo(f);
                backups.Add(new { Name = fi.Name, Size = fi.Length, Time = fi.CreationTimeUtc });
            }
            ViewBag.Backups = backups;
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackup()
        {
            try
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var fileName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                var path = Path.Combine(dir, fileName);

                var export = new Dictionary<string, object?>();
                export["Users"] = await _db.Users.AsNoTracking().ToListAsync();
                export["Roles"] = await _db.Roles.AsNoTracking().ToListAsync();
                export["UserRoles"] = await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AsNoTracking().ToListAsync();
                export["Warehouses"] = await _db.Warehouses.AsNoTracking().ToListAsync();
                export["ProductModels"] = await _db.ProductModels.AsNoTracking().ToListAsync();
                export["SkuVariants"] = await _db.SkuVariants.AsNoTracking().ToListAsync();
                export["StockLedgers"] = await _db.StockLedgers.AsNoTracking().ToListAsync();
                export["StockMovements"] = await _db.StockMovements.AsNoTracking().ToListAsync();
                export["InventoryVouchers"] = await _db.InventoryVouchers.AsNoTracking().ToListAsync();
                export["VoucherLines"] = await _db.VoucherLines.AsNoTracking().ToListAsync();
                export["AuditLogs"] = await _db.AuditLogs.AsNoTracking().ToListAsync();
                export["ComboProducts"] = await _db.ComboProducts.AsNoTracking().ToListAsync();
                export["BomComponents"] = await _db.BomComponents.AsNoTracking().ToListAsync();
                export["StockTransfers"] = await _db.StockTransfers.AsNoTracking().ToListAsync();
                export["StockTransferLines"] = await _db.StockTransferLines.AsNoTracking().ToListAsync();
                export["StockAdjustments"] = await _db.StockAdjustments.AsNoTracking().ToListAsync();
                export["DamagedRecords"] = await _db.DamagedRecords.AsNoTracking().ToListAsync();
                export["ApprovalSteps"] = await _db.ApprovalSteps.AsNoTracking().ToListAsync();
                export["AiSuggestions"] = await _db.AiSuggestions.AsNoTracking().ToListAsync();
                export["PurchaseOrders"] = await _db.PurchaseOrders.AsNoTracking().ToListAsync();
                export["PurchaseOrderLines"] = await _db.PurchaseOrderLines.AsNoTracking().ToListAsync();
                export["SalesOrders"] = await _db.SalesOrders.AsNoTracking().ToListAsync();
                export["SalesOrderLines"] = await _db.SalesOrderLines.AsNoTracking().ToListAsync();

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                var json = System.Text.Json.JsonSerializer.Serialize(export, jsonOptions);
                await System.IO.File.WriteAllTextAsync(path, json);

                TempData["Message"] = "Backup completed.";
                return RedirectToAction("Backup");
            }
            catch (Exception ex)
            {
                TempData["Error"] = $"Lỗi tạo sao lưu: {ex.Message}";
                return RedirectToAction("Backup");
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBackupAjax()
        {
            try
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var fileName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                var path = Path.Combine(dir, fileName);

                var export = new Dictionary<string, object?>();
                export["Users"] = await _db.Users.AsNoTracking().ToListAsync();
                export["Roles"] = await _db.Roles.AsNoTracking().ToListAsync();
                export["UserRoles"] = await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AsNoTracking().ToListAsync();
                export["Warehouses"] = await _db.Warehouses.AsNoTracking().ToListAsync();
                export["ProductModels"] = await _db.ProductModels.AsNoTracking().ToListAsync();
                export["SkuVariants"] = await _db.SkuVariants.AsNoTracking().ToListAsync();
                export["StockLedgers"] = await _db.StockLedgers.AsNoTracking().ToListAsync();
                export["StockMovements"] = await _db.StockMovements.AsNoTracking().ToListAsync();
                export["InventoryVouchers"] = await _db.InventoryVouchers.AsNoTracking().ToListAsync();
                export["VoucherLines"] = await _db.VoucherLines.AsNoTracking().ToListAsync();
                export["AuditLogs"] = await _db.AuditLogs.AsNoTracking().ToListAsync();
                export["ComboProducts"] = await _db.ComboProducts.AsNoTracking().ToListAsync();
                export["BomComponents"] = await _db.BomComponents.AsNoTracking().ToListAsync();
                export["StockTransfers"] = await _db.StockTransfers.AsNoTracking().ToListAsync();
                export["StockTransferLines"] = await _db.StockTransferLines.AsNoTracking().ToListAsync();
                export["StockAdjustments"] = await _db.StockAdjustments.AsNoTracking().ToListAsync();
                export["DamagedRecords"] = await _db.DamagedRecords.AsNoTracking().ToListAsync();
                export["ApprovalSteps"] = await _db.ApprovalSteps.AsNoTracking().ToListAsync();
                export["AiSuggestions"] = await _db.AiSuggestions.AsNoTracking().ToListAsync();
                export["PurchaseOrders"] = await _db.PurchaseOrders.AsNoTracking().ToListAsync();
                export["PurchaseOrderLines"] = await _db.PurchaseOrderLines.AsNoTracking().ToListAsync();
                export["SalesOrders"] = await _db.SalesOrders.AsNoTracking().ToListAsync();
                export["SalesOrderLines"] = await _db.SalesOrderLines.AsNoTracking().ToListAsync();

                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                var json = System.Text.Json.JsonSerializer.Serialize(export, jsonOptions);
                await System.IO.File.WriteAllTextAsync(path, json);

                return Json(new { success = true, file = fileName });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, error = ex.Message });
            }
        }

        [HttpGet]
        public IActionResult DownloadBackup(string name)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
            var path = Path.Combine(dir, name ?? string.Empty);
            if (!System.IO.File.Exists(path)) return NotFound();
            var bytes = System.IO.File.ReadAllBytes(path);
            // Encrypt and compute HMAC before sending to client
            var encrypted = EncryptAndWrap(bytes);
            var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(encrypted);
            var outName = name + ".enc";
            return File(payload, "application/octet-stream", outName);
        }

        // --- Encryption / HMAC helpers for secure download / anti-tamper check ---
        private static readonly string BackupSecretPhrase = "DuAnCode_Backup_Secret_Key_ChangeMe";
        private static byte[] GetKey()
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(Encoding.UTF8.GetBytes(BackupSecretPhrase));
        }

        private static Dictionary<string, string> EncryptAndWrap(byte[] plain)
        {
            var key = GetKey();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.GenerateIV();
            byte[] cipher;
            using (var ms = new MemoryStream())
            using (var cs = new CryptoStream(ms, aes.CreateEncryptor(), CryptoStreamMode.Write))
            {
                cs.Write(plain, 0, plain.Length);
                cs.FlushFinalBlock();
                cipher = ms.ToArray();
            }
            // packaged = IV + cipher
            var packaged = new byte[aes.IV.Length + cipher.Length];
            Buffer.BlockCopy(aes.IV, 0, packaged, 0, aes.IV.Length);
            Buffer.BlockCopy(cipher, 0, packaged, aes.IV.Length, cipher.Length);

            var hmac = ComputeHmac(packaged);
            return new Dictionary<string, string>
            {
                ["data"] = Convert.ToBase64String(packaged),
                ["hash"] = Convert.ToBase64String(hmac)
            };
        }

        private static byte[] DecryptAes(byte[] packaged)
        {
            var key = GetKey();
            // extract IV (first 16 bytes)
            var iv = packaged.Take(16).ToArray();
            var cipher = packaged.Skip(16).ToArray();
            using var aes = Aes.Create();
            aes.Key = key;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            using var ms = new MemoryStream();
            using (var cs = new CryptoStream(ms, aes.CreateDecryptor(), CryptoStreamMode.Write))
            {
                cs.Write(cipher, 0, cipher.Length);
                cs.FlushFinalBlock();
            }
            return ms.ToArray();
        }

        private static byte[] ComputeHmac(byte[] data)
        {
            var key = GetKey();
            using var h = new HMACSHA256(key);
            return h.ComputeHash(data);
        }

        private static bool VerifyHmac(byte[] data, byte[] expectedHmac)
        {
            var actual = ComputeHmac(data);
            return CryptographicOperations.FixedTimeEquals(actual, expectedHmac);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteBackup(string name)
        {
            var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
            var path = Path.Combine(dir, name ?? string.Empty);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            TempData["Message"] = "Deleted backup.";
            return RedirectToAction("Backup");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RestoreBackup(string fileName, IFormFile? externalBackupFile)
        {
            byte[] contentBytes;
            string jsonText;
            // Capture current user info to validate session after restore
            var currentUser = await _userManager.GetUserAsync(User);
            var currentUserId = currentUser?.Id;
            var currentUserPasswordHash = currentUser?.PasswordHash;

            // create a temp backup of current DB state before destructive restore
            var backupsDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
            if (!Directory.Exists(backupsDir)) Directory.CreateDirectory(backupsDir);
            var tempBackupPath = Path.Combine(backupsDir, "temp_snap.json");
            try
            {
                var snapshot = new Dictionary<string, object?>();
                snapshot["Users"] = await _db.Users.AsNoTracking().ToListAsync();
                snapshot["Roles"] = await _db.Roles.AsNoTracking().ToListAsync();
                snapshot["UserRoles"] = await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AsNoTracking().ToListAsync();
                snapshot["Warehouses"] = await _db.Warehouses.AsNoTracking().ToListAsync();
                snapshot["ProductModels"] = await _db.ProductModels.AsNoTracking().ToListAsync();
                snapshot["SkuVariants"] = await _db.SkuVariants.AsNoTracking().ToListAsync();
                snapshot["StockLedgers"] = await _db.StockLedgers.AsNoTracking().ToListAsync();
                snapshot["StockMovements"] = await _db.StockMovements.AsNoTracking().ToListAsync();
                snapshot["InventoryVouchers"] = await _db.InventoryVouchers.AsNoTracking().ToListAsync();
                snapshot["VoucherLines"] = await _db.VoucherLines.AsNoTracking().ToListAsync();
                snapshot["AuditLogs"] = await _db.AuditLogs.AsNoTracking().ToListAsync();
                var opts = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                var snapJson = System.Text.Json.JsonSerializer.Serialize(snapshot, opts);
                await System.IO.File.WriteAllTextAsync(tempBackupPath, snapJson);
            }
            catch
            {
                // if temp backup creation fails, abort restore early
                TempData["Error"] = "Unable to create temporary backup before restore. Aborting.";
                return RedirectToAction("Backup");
            }

            try
            {
                if (externalBackupFile != null && externalBackupFile.Length > 0)
                {
                    using var ms = new MemoryStream();
                    await externalBackupFile.CopyToAsync(ms);
                    contentBytes = ms.ToArray();
                }
                else if (!string.IsNullOrWhiteSpace(fileName))
                {
                    var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
                    var path = Path.Combine(dir, fileName);
                    if (!System.IO.File.Exists(path)) { TempData["Error"] = "Selected backup file not found on server."; return RedirectToAction("Backup"); }
                    contentBytes = await System.IO.File.ReadAllBytesAsync(path);
                }
                else
                {
                    TempData["Error"] = "No file selected for restore.";
                    return RedirectToAction("Backup");
                }

                // Detect and handle encrypted wrapper
                bool isWrapped = false;
                try { using var probe = System.Text.Json.JsonDocument.Parse(contentBytes); isWrapped = probe.RootElement.TryGetProperty("data", out _) && probe.RootElement.TryGetProperty("hash", out _); } catch { isWrapped = false; }
                if (isWrapped)
                {
                    var wrapper = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(contentBytes) ?? new();
                    if (!wrapper.TryGetValue("data", out var dataB64) || !wrapper.TryGetValue("hash", out var hashB64)) { TempData["Error"] = "Invalid encrypted backup format."; return RedirectToAction("Backup"); }
                    var packaged = Convert.FromBase64String(dataB64);
                    var receivedHmac = Convert.FromBase64String(hashB64);
                    if (!VerifyHmac(packaged, receivedHmac)) { TempData["Error"] = "❌ CHI LỖI: File backup không hợp lệ hoặc đã bị thay đổi (Tampered)!"; return RedirectToAction("Backup"); }
                    var plain = DecryptAes(packaged);
                    jsonText = Encoding.UTF8.GetString(plain);
                }
                else
                {
                    jsonText = Encoding.UTF8.GetString(contentBytes);
                }

                // Parse JSON root
                using var doc = System.Text.Json.JsonDocument.Parse(jsonText);

                // Begin transaction and wipe DB tables in proper order
                using var transaction = await _db.Database.BeginTransactionAsync();
                try
                {
                    // Delete child tables first to respect FKs
                    var deleteOrder = new[] {
                        "VoucherLines",
                        "InventoryVouchers",
                        "VoucherLines",
                        "VoucherLines",
                        "AuditLogs",
                        "StockMovements",
                        "StockLedgers",
                        "StockTransferLines",
                        "StockTransfers",
                        "PurchaseOrderLines",
                        "PurchaseOrders",
                        "SalesOrderLines",
                        "SalesOrders",
                        "VoucherLines",
                        "StockAdjustments",
                        "DamagedRecords",
                        "ApprovalSteps",
                        "AiSuggestions",
                        "ComboProducts",
                        "BomComponents",
                        "SkuVariants",
                        "ProductModels",
                        "Suppliers",
                        "Warehouses",
                    };
                    foreach (var t in deleteOrder)
                    {
                        try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [" + t + "]"); } catch { }
                    }

                    // Identity related deletions
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserRoles]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserTokens]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserLogins]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUserClaims]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetRoleClaims]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetRoles]"); } catch { }
                    try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [AspNetUsers]"); } catch { }

                    await _db.SaveChangesAsync();

                    // Import entities from JSON if present
                    // Roles
                    if (doc.RootElement.TryGetProperty("Roles", out var rolesEl))
                    {
                        try
                        {
                            var roles = System.Text.Json.JsonSerializer.Deserialize<List<Role>>(rolesEl.GetRawText());
                            if (roles != null && roles.Any()) { await _db.Roles.AddRangeAsync(roles); await _db.SaveChangesAsync(); }
                        }
                        catch { }
                    }

                    // Users
                    if (doc.RootElement.TryGetProperty("Users", out var usersEl))
                    {
                        try
                        {
                            var users = System.Text.Json.JsonSerializer.Deserialize<List<User>>(usersEl.GetRawText());
                            if (users != null && users.Any()) 
                            { 
                                var hasher = new Microsoft.AspNetCore.Identity.PasswordHasher<User>();
                                foreach(var u in users)
                                {
                                    if (string.IsNullOrEmpty(u.PasswordHash))
                                    {
                                        u.PasswordHash = hasher.HashPassword(u, "ChangeMe123!");
                                    }
                                    if (string.IsNullOrEmpty(u.SecurityStamp))
                                    {
                                        u.SecurityStamp = Guid.NewGuid().ToString();
                                    }
                                }
                                await _db.Users.AddRangeAsync(users); 
                                await _db.SaveChangesAsync(); 
                            }
                        }
                        catch { }
                    }

                    // UserRoles
                    if (doc.RootElement.TryGetProperty("UserRoles", out var urEl))
                    {
                        try
                        {
                            var urs = System.Text.Json.JsonSerializer.Deserialize<List<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>>(urEl.GetRawText());
                            if (urs != null && urs.Any()) { await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AddRangeAsync(urs); await _db.SaveChangesAsync(); }
                        }
                        catch { }
                    }

                    // Warehouses
                    if (doc.RootElement.TryGetProperty("Warehouses", out var whsEl))
                    {
                        try { var whs = System.Text.Json.JsonSerializer.Deserialize<List<Warehouse>>(whsEl.GetRawText()); if (whs != null && whs.Any()) { await _db.Warehouses.AddRangeAsync(whs); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // ProductModels
                    if (doc.RootElement.TryGetProperty("ProductModels", out var pmEl))
                    {
                        try { var pms = System.Text.Json.JsonSerializer.Deserialize<List<ProductModel>>(pmEl.GetRawText()); if (pms != null && pms.Any()) { await _db.ProductModels.AddRangeAsync(pms); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // SkuVariants
                    if (doc.RootElement.TryGetProperty("SkuVariants", out var skEl))
                    {
                        try { var sks = System.Text.Json.JsonSerializer.Deserialize<List<SkuVariant>>(skEl.GetRawText()); if (sks != null && sks.Any()) { await _db.SkuVariants.AddRangeAsync(sks); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // StockLedgers
                    if (doc.RootElement.TryGetProperty("StockLedgers", out var slEl))
                    {
                        try { var sls = System.Text.Json.JsonSerializer.Deserialize<List<StockLedger>>(slEl.GetRawText()); if (sls != null && sls.Any()) { await _db.StockLedgers.AddRangeAsync(sls); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // StockMovements
                    if (doc.RootElement.TryGetProperty("StockMovements", out var smEl))
                    {
                        try { var sms = System.Text.Json.JsonSerializer.Deserialize<List<StockMovement>>(smEl.GetRawText()); if (sms != null && sms.Any()) { await _db.StockMovements.AddRangeAsync(sms); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // InventoryVouchers
                    if (doc.RootElement.TryGetProperty("InventoryVouchers", out var ivEl))
                    {
                        try { var ivs = System.Text.Json.JsonSerializer.Deserialize<List<InventoryVoucher>>(ivEl.GetRawText()); if (ivs != null && ivs.Any()) { await _db.InventoryVouchers.AddRangeAsync(ivs); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // VoucherLines
                    if (doc.RootElement.TryGetProperty("VoucherLines", out var vlEl))
                    {
                        try { var vls = System.Text.Json.JsonSerializer.Deserialize<List<VoucherLine>>(vlEl.GetRawText()); if (vls != null && vls.Any()) { await _db.VoucherLines.AddRangeAsync(vls); await _db.SaveChangesAsync(); } } catch { }
                    }

                    // Other optional entities: PurchaseOrders, SalesOrders, Transfers, etc.
                    if (doc.RootElement.TryGetProperty("PurchaseOrders", out var poEl)) { try { var pos = System.Text.Json.JsonSerializer.Deserialize<List<PurchaseOrder>>(poEl.GetRawText()); if (pos != null && pos.Any()) { await _db.PurchaseOrders.AddRangeAsync(pos); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("PurchaseOrderLines", out var polEl)) { try { var pols = System.Text.Json.JsonSerializer.Deserialize<List<PurchaseOrderLine>>(polEl.GetRawText()); if (pols != null && pols.Any()) { await _db.PurchaseOrderLines.AddRangeAsync(pols); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("SalesOrders", out var soEl)) { try { var sos = System.Text.Json.JsonSerializer.Deserialize<List<SalesOrder>>(soEl.GetRawText()); if (sos != null && sos.Any()) { await _db.SalesOrders.AddRangeAsync(sos); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("SalesOrderLines", out var solEl)) { try { var sols = System.Text.Json.JsonSerializer.Deserialize<List<SalesOrderLine>>(solEl.GetRawText()); if (sols != null && sols.Any()) { await _db.SalesOrderLines.AddRangeAsync(sols); await _db.SaveChangesAsync(); } } catch { } }

                    if (doc.RootElement.TryGetProperty("ComboProducts", out var cpEl)) { try { var cps = System.Text.Json.JsonSerializer.Deserialize<List<ComboProduct>>(cpEl.GetRawText()); if (cps != null && cps.Any()) { await _db.ComboProducts.AddRangeAsync(cps); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("BomComponents", out var bcEl)) { try { var bcs = System.Text.Json.JsonSerializer.Deserialize<List<BomComponent>>(bcEl.GetRawText()); if (bcs != null && bcs.Any()) { await _db.BomComponents.AddRangeAsync(bcs); await _db.SaveChangesAsync(); } } catch { } }

                    if (doc.RootElement.TryGetProperty("StockTransfers", out var stEl)) { try { var sts = System.Text.Json.JsonSerializer.Deserialize<List<StockTransfer>>(stEl.GetRawText()); if (sts != null && sts.Any()) { await _db.StockTransfers.AddRangeAsync(sts); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("StockTransferLines", out var stlEl)) { try { var stls = System.Text.Json.JsonSerializer.Deserialize<List<StockTransferLine>>(stlEl.GetRawText()); if (stls != null && stls.Any()) { await _db.StockTransferLines.AddRangeAsync(stls); await _db.SaveChangesAsync(); } } catch { } }

                    if (doc.RootElement.TryGetProperty("StockAdjustments", out var saEl)) { try { var sas = System.Text.Json.JsonSerializer.Deserialize<List<StockAdjustment>>(saEl.GetRawText()); if (sas != null && sas.Any()) { await _db.StockAdjustments.AddRangeAsync(sas); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("DamagedRecords", out var drEl)) { try { var drs = System.Text.Json.JsonSerializer.Deserialize<List<DamagedRecord>>(drEl.GetRawText()); if (drs != null && drs.Any()) { await _db.DamagedRecords.AddRangeAsync(drs); await _db.SaveChangesAsync(); } } catch { } }

                    if (doc.RootElement.TryGetProperty("ApprovalSteps", out var apEl)) { try { var aps = System.Text.Json.JsonSerializer.Deserialize<List<ApprovalStep>>(apEl.GetRawText()); if (aps != null && aps.Any()) { await _db.ApprovalSteps.AddRangeAsync(aps); await _db.SaveChangesAsync(); } } catch { } }
                    if (doc.RootElement.TryGetProperty("AiSuggestions", out var aiEl)) { try { var ais = System.Text.Json.JsonSerializer.Deserialize<List<AiSuggestion>>(aiEl.GetRawText()); if (ais != null && ais.Any()) { await _db.AiSuggestions.AddRangeAsync(ais); await _db.SaveChangesAsync(); } } catch { } }

                    if (doc.RootElement.TryGetProperty("AuditLogs", out var alEl)) { try { var als = System.Text.Json.JsonSerializer.Deserialize<List<AuditLog>>(alEl.GetRawText()); if (als != null && als.Any()) { await _db.AuditLogs.AddRangeAsync(als); await _db.SaveChangesAsync(); } } catch { } }

                    // After imports, ensure roles and admin user integrity
                    var rolesExist = await _db.Roles.AnyAsync();
                    if (!rolesExist)
                    {
                        // if roles table is empty, seed minimal roles
                        await SeedAdminRoleAsync();
                    }

                    // Validate that Admin role exists and 'admin' user has it
                    var adminRoleExists = await _db.Roles.AnyAsync(r => r.Name == "Admin");
                    var adminUser = await _db.Users.FirstOrDefaultAsync(u => u.UserName == "admin");
                    var adminHasRole = false;
                    if (adminUser != null && adminRoleExists)
                    {
                        adminHasRole = await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AnyAsync(ur => ur.UserId == adminUser.Id && _db.Roles.Any(r => r.Id == ur.RoleId && r.Name == "Admin"));
                    }

                    if (!adminRoleExists || adminUser == null || !adminHasRole)
                    {
                        // attempt to repair minimal admin role/user linkage
                        await SeedAdminRoleAsync();
                        // ensure admin user is assigned Admin role via UserManager if possible
                        try
                        {
                            if (adminUser == null)
                            {
                                adminUser = await _db.Users.FirstOrDefaultAsync(u => u.UserName == "admin");
                            }
                            if (adminUser != null)
                            {
                                var has = await _userManager.IsInRoleAsync(adminUser, "Admin");
                                if (!has)
                                {
                                    await _userManager.AddToRoleAsync(adminUser, "Admin");
                                }
                            }
                        }
                        catch
                        {
                            // ignore — will sign out user later
                        }
                    }

                    // Ensure at least one user exists
                    var anyUsers = await _db.Users.AnyAsync();
                    if (!anyUsers)
                    {
                        await transaction.RollbackAsync();
                        TempData["Error"] = "File backup không chứa dữ liệu hợp lệ (no users).";
                        return RedirectToAction("Backup");
                    }

                    // Commit transaction
                    await transaction.CommitAsync();
                    // restore succeeded, delete temp backup
                    try { if (System.IO.File.Exists(tempBackupPath)) System.IO.File.Delete(tempBackupPath); } catch { }

                    // Ensure Admin role assigned to any 'admin' accounts even if UserRoles table was missing in backup
                    try
                    {
                        // ensure Admin role exists
                        var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
                        if (adminRole == null)
                        {
                            await SeedAdminRoleAsync();
                            adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
                        }

                        var adminUsers = await _db.Users.Where(u => u.UserName != null && (u.UserName.ToLower() == "admin" || u.UserName.ToLower() == "system administrator")).ToListAsync();
                        foreach (var au in adminUsers)
                        {
                            try
                            {
                                if (!await _userManager.IsInRoleAsync(au, "Admin"))
                                {
                                    await _userManager.AddToRoleAsync(au, "Admin");
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                    // noop: patch consistency marker
                }
                catch
                {
                    await transaction.RollbackAsync();
                    // Attempt to restore from temp snapshot to return DB to prior state
                    try
                    {
                        if (System.IO.File.Exists(tempBackupPath))
                        {
                            var tempJson = await System.IO.File.ReadAllTextAsync(tempBackupPath);
                            using var tempDoc = System.Text.Json.JsonDocument.Parse(tempJson);
                            using var rollbackTransaction = await _db.Database.BeginTransactionAsync();
                            try
                            {
                                // wipe whatever partially changed (best effort)
                                var tables = new[] { "VoucherLines","InventoryVouchers","AuditLogs","StockMovements","StockLedgers","SkuVariants","ProductModels","Warehouses","AspNetUserRoles","AspNetUserTokens","AspNetUserLogins","AspNetUserClaims","AspNetRoleClaims","AspNetRoles","AspNetUsers" };
                                foreach (var t in tables) { try { await _db.Database.ExecuteSqlRawAsync("DELETE FROM [" + t + "]"); } catch { } }
                                // call a local import helper implemented below
                                await ImportEntitiesFromDocumentAsync(tempDoc.RootElement);
                                await rollbackTransaction.CommitAsync();
                            }
                            catch
                            {
                                await rollbackTransaction.RollbackAsync();
                                // if rollback import fails, report critical error
                                TempData["Error"] = "❌ Khôi phục thất bại và hệ thống không thể tự động quay về trạng thái trước đó. Liên hệ quản trị viên.";
                                return RedirectToAction("Backup");
                            }
                        }
                    }
                    catch
                    {
                        // ignore
                    }
                    TempData["Error"] = "❌ Khôi phục thất bại: File backup không đúng định dạng. Hệ thống đã tự động quay về trạng thái dữ liệu an toàn trước đó.";
                    return RedirectToAction("Backup");
                }

                // After restore, refresh sign-in for current user so claims/roles update (or sign out)
                if (!string.IsNullOrWhiteSpace(currentUserId))
                {
                    var postUser = await _userManager.FindByIdAsync(currentUserId);
                    if (postUser == null)
                    {
                        await _signInManager.SignOutAsync();
                        return RedirectToAction("Login", "Account", new { message = "Khôi phục dữ liệu thành công! Vui lòng đăng nhập lại để cập nhật toàn bộ menu chức năng." });
                    }
                    // After restore, force sign-out and clear any TempData flags, then redirect to Login to force fresh sign-in
                    try { await _signInManager.SignOutAsync(); } catch { try { await HttpContext.SignOutAsync(); } catch { } }
                    // clear TempData to avoid showing stale alerts
                    TempData.Remove("Success");
                    TempData.Remove("Error");
                    return RedirectToAction("Login", "Account", new { message = "Restored" });
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Restore failed: " + ex.Message;
                return RedirectToAction("Backup");
            }
            // default fallback return to ensure all code paths return an IActionResult
            return RedirectToAction("Login", "Account");
        }
    }
}
