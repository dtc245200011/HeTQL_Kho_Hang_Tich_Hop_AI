using Microsoft.Extensions.DependencyInjection;
using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    public static class AdminControllerExtensions
    {
        // Ensure admin user has Admin role; used at startup or on-demand
        public static async Task EnsureAdminIntegrityAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var _db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<User>>();

            var adminRole = await _db.Roles.FirstOrDefaultAsync(r => r.Name == "Admin");
            if (adminRole == null)
            {
                adminRole = new Role { Id = Guid.NewGuid().ToString(), Name = "Admin", NormalizedName = "ADMIN" };
                await _db.Roles.AddAsync(adminRole);
                await _db.SaveChangesAsync();
            }

            // Use UserManager to create admin user if not exists (idempotent)
            var adminUser = await userManager.FindByNameAsync("admin");
            if (adminUser == null)
            {
                adminUser = new User { UserName = "admin", Email = "admin@example.com", EmailConfirmed = true, FullName = "System Administrator" };
                var createRes = await userManager.CreateAsync(adminUser, "ChangeMe123!");
                if (!createRes.Succeeded)
                {
                    // fallback: try to retrieve any existing admin by username or email
                    adminUser = await userManager.FindByNameAsync("admin") ?? await userManager.FindByEmailAsync("admin@example.com");
                }
            }

            var exists = await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AnyAsync(ur => ur.UserId == adminUser!.Id && ur.RoleId == adminRole!.Id);
            if (!exists)
            {
                try
                {
                    // Use parameterized form to avoid interpolation injection warnings
                    await _db.Database.ExecuteSqlRawAsync("INSERT INTO [AspNetUserRoles] (UserId, RoleId) VALUES ({0}, {1})", adminUser!.Id, adminRole!.Id);
                }
                catch
                {
                    var ur = new Microsoft.AspNetCore.Identity.IdentityUserRole<string> { UserId = adminUser!.Id, RoleId = adminRole!.Id };
                    await _db.Set<Microsoft.AspNetCore.Identity.IdentityUserRole<string>>().AddAsync(ur);
                    await _db.SaveChangesAsync();
                }
            }
        }
    }
}
