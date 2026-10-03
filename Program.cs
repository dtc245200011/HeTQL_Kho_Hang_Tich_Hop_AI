using DuAnCode.Web.Data;
using DuAnCode.Web.Repositories;
using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<DuAnCode.Web.Filters.AuditActionFilter>();
});

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

builder.Services.AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>));
builder.Services.AddScoped<IInventoryRepository, InventoryRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IInventoryService, InventoryService>();
builder.Services.AddScoped<IAiService, AiService>();
builder.Services.AddScoped<IInventoryServiceAdvanced, InventoryServiceAdvanced>();
builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
builder.Services.AddScoped<IDamagedService, DamagedService>();
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(c => { c.BaseAddress = new Uri("http://localhost:11434"); });
builder.Services.AddHostedService<DuAnCode.Web.Background.AiMonitoringService>();

// Identity
builder.Services.AddIdentity<DuAnCode.Web.Models.User, DuAnCode.Web.Models.Role>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    ;

// Configure application cookie to redirect to Login / AccessDenied handlers
builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
});

builder.Services.AddHostedService<DuAnCode.Web.Services.OllamaStartupService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    db.Database.Migrate();
    var roleManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.RoleManager<DuAnCode.Web.Models.Role>>();
    var userManager = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<DuAnCode.Web.Models.User>>();
    // Use DbSeeder to perform seeding via service provider
    await DuAnCode.Web.Data.DbSeeder.SeedAsync(app.Services);
    // Ensure admin integrity at startup
    try
    {
        await DuAnCode.Web.Controllers.AdminControllerExtensions.EnsureAdminIntegrityAsync(scope.ServiceProvider);
    }
    catch
    {
        // ignore startup self-heal failures
    }

    // Auto-fix missing columns that older DBs may lack to avoid SqlException at runtime.
    try
    {
        // 1. Ensure AuditLogs has an 'Id' column (some deployments expect it)
        await db.Database.ExecuteSqlRawAsync(@" 
            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'AuditLogs') 
               AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('AuditLogs') AND name = 'Id')
            BEGIN
                ALTER TABLE AuditLogs ADD Id NVARCHAR(450) NULL;
            END
        ");

        // 2. Ensure Warehouses has a 'Location' column
        await db.Database.ExecuteSqlRawAsync(@" 
            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'Warehouses') 
               AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('Warehouses') AND name = 'Location')
            BEGIN
                ALTER TABLE Warehouses ADD Location NVARCHAR(255) NULL;
            END
        ");
    }
    catch
    {
        // Ignore database-specific failures (e.g., not SQL Server) so app still starts.
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    // In development enable detailed exception page for easier debugging
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
