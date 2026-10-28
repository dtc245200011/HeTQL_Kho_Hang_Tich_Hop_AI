using DuAnCode.Web.Data;
using DuAnCode.Web.Repositories;
using DuAnCode.Web.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
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
builder.Services.AddScoped<IAiChatGroundingService, AiChatGroundingService>();
builder.Services.AddScoped<IInventoryServiceAdvanced, InventoryServiceAdvanced>();
builder.Services.AddScoped<IApprovalWorkflowService, ApprovalWorkflowService>();
builder.Services.AddScoped<IDamagedService, DamagedService>();
builder.Services.AddTransient<DuAnCode.Web.Services.IEmailSender, DuAnCode.Web.Services.SmtpEmailSender>();
builder.Services.AddHttpClient<IOllamaClient, OllamaClient>(c => { c.BaseAddress = new Uri("http://localhost:11434"); });
builder.Services.AddHostedService<DuAnCode.Web.Background.AiMonitoringService>();
builder.Services.AddHostedService<DuAnCode.Web.Background.DatabaseBackupService>();

// Identity
builder.Services.AddIdentity<DuAnCode.Web.Models.User, DuAnCode.Web.Models.Role>(options =>
{
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.User.RequireUniqueEmail = false;
})
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

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

        // 3. Ensure SystemConfigs has Approval Workflow columns
        await db.Database.ExecuteSqlRawAsync(@"
            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'SystemConfigs')
            BEGIN
                IF NOT EXISTS (SELECT * FROM sys.columns WHERE Name = N'EnableMultiLevelApproval' AND Object_ID = Object_ID(N'SystemConfigs'))
                BEGIN
                    ALTER TABLE SystemConfigs ADD 
                        EnableMultiLevelApproval BIT NOT NULL DEFAULT 1,
                        OutboundDirectorApprovalThreshold DECIMAL(18,2) NOT NULL DEFAULT 100000000,
                        InboundRequireApproval BIT NOT NULL DEFAULT 1,
                        TransferRequireApproval BIT NOT NULL DEFAULT 1,
                        ApprovalRoleLevel1 NVARCHAR(50) NOT NULL DEFAULT 'Accountant',
                        ApprovalRoleLevel2 NVARCHAR(50) NOT NULL DEFAULT 'WarehouseManager',
                        ApprovalRoleLevel3 NVARCHAR(50) NOT NULL DEFAULT 'Director',
                        AutoApproveLowValueOutbound BIT NOT NULL DEFAULT 0,
                        AutoApproveThreshold DECIMAL(18,2) NOT NULL DEFAULT 0;
                END
            END
        ");

        // 4. Ensure InventoryAuditLines has a 'StockStatus' column
        await db.Database.ExecuteSqlRawAsync(@" 
            IF EXISTS (SELECT * FROM sys.tables WHERE name = 'InventoryAuditLines') 
               AND NOT EXISTS (SELECT * FROM sys.columns WHERE object_id = OBJECT_ID('InventoryAuditLines') AND name = 'StockStatus')
            BEGIN
                ALTER TABLE InventoryAuditLines ADD StockStatus NVARCHAR(50) NOT NULL DEFAULT 'GOOD';
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

app.Use(async (context, next) =>
{
    using (var scope = context.RequestServices.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<DuAnCode.Web.Data.ApplicationDbContext>();
        var config = await db.SystemConfigs.AsNoTracking().FirstOrDefaultAsync();
        if (config != null)
        {
            if (!string.IsNullOrEmpty(config.DefaultLanguage))
            {
                try
                {
                    var culture = new System.Globalization.CultureInfo(config.DefaultLanguage);
                    if (config.CurrencyFormat == "VND")
                        culture.NumberFormat.CurrencySymbol = "₫";
                    else if (config.CurrencyFormat == "USD")
                        culture.NumberFormat.CurrencySymbol = "$";
                    
                    System.Globalization.CultureInfo.CurrentCulture = culture;
                    System.Globalization.CultureInfo.CurrentUICulture = culture;
                }
                catch { }
            }

            context.Items["CurrencyFormat"] = config.CurrencyFormat ?? "VND";
            context.Items["CurrencySymbol"] = config.CurrencyFormat == "USD" ? "$" : "₫";
            context.Items["CompanyName"] = !string.IsNullOrWhiteSpace(config.CompanyName) ? config.CompanyName : "DuAnCode WMS";
            context.Items["CompanyAddress"] = config.Address ?? string.Empty;
            context.Items["CompanyPhone"] = config.Phone ?? string.Empty;
            context.Items["CompanyEmail"] = config.Email ?? string.Empty;
        }
    }
    await next(context);
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// Middleware: Enforce MaintenanceMode and StopApi settings
app.Use(async (context, next) =>
{
    var path = context.Request.Path;

    // Skip static assets, error page, maintenance page, login/logout/profile
    if (path.StartsWithSegments("/css") || path.StartsWithSegments("/js") ||
        path.StartsWithSegments("/lib") || path.StartsWithSegments("/images") ||
        path.StartsWithSegments("/favicon.ico") || path.StartsWithSegments("/Home/Error") ||
        path.StartsWithSegments("/Home/Maintenance") || path.StartsWithSegments("/Account/Login") ||
        path.StartsWithSegments("/Account/Logout") || path.StartsWithSegments("/Account/AccessDenied"))
    {
        await next(context);
        return;
    }

    using (var scope = context.RequestServices.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<DuAnCode.Web.Data.ApplicationDbContext>();
        var config = await db.SystemConfigs.AsNoTracking().FirstOrDefaultAsync();
        if (config != null)
        {
            bool isAdmin = context.User.Identity?.IsAuthenticated == true &&
                           (context.User.IsInRole("Admin") || context.User.IsInRole("Quản trị viên") ||
                            context.User.IsInRole("Director") || context.User.IsInRole("Giám đốc"));

            // 1. Check StopApi: Lock third-party API access
            if (config.StopApi && path.StartsWithSegments("/api"))
            {
                // Internal AI chat allowed for web users, otherwise block API calls
                if (!isAdmin && !path.StartsWithSegments("/api/AiChat"))
                {
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"ok\":false,\"status\":503,\"message\":\"Cổng API kết nối từ bên thứ ba hiện đang tạm khóa bởi Quản trị viên hệ thống.\"}");
                    return;
                }
            }

            // 2. Check MaintenanceMode: Lock out non-admin employees from accessing normal pages
            if (config.MaintenanceMode && !isAdmin)
            {
                if (path.StartsWithSegments("/api") || context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
                {
                    context.Response.StatusCode = 503;
                    context.Response.ContentType = "application/json; charset=utf-8";
                    await context.Response.WriteAsync("{\"ok\":false,\"status\":503,\"message\":\"Hệ thống đang trong chế độ bảo trì. Chỉ Quản trị viên mới được phép truy cập.\"}");
                    return;
                }

                context.Response.Redirect("/Home/Maintenance");
                return;
            }
        }
    }

    await next(context);
});

app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
