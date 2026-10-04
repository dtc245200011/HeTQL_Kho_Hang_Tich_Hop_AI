using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace DuAnCode.Web.Background
{
    public class DatabaseBackupService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<DatabaseBackupService> _logger;
        // Kiểm tra mỗi giờ 1 lần
        private readonly TimeSpan _checkInterval = TimeSpan.FromHours(1);

        public DatabaseBackupService(IServiceProvider sp, ILogger<DatabaseBackupService> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("DatabaseBackupService is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var config = await db.SystemConfigs.FirstOrDefaultAsync(stoppingToken);

                    // Nếu bật AutoBackup và bây giờ là khoảng từ 2h-3h sáng (thời điểm vắng khách)
                    // Hoặc ta có thể lưu ngày backup cuối cùng vào DB để tránh backup nhiều lần trong 1 ngày.
                    // Để đơn giản, giả sử nếu cấu hình AutoBackup = true, ta sẽ kiểm tra xem hôm nay đã backup chưa
                    if (config != null && config.AutoBackup)
                    {
                        await PerformBackupAsync(db);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred while executing AutoBackup.");
                }

                // Đợi đến chu kỳ kiểm tra tiếp theo (1 giờ)
                await Task.Delay(_checkInterval, stoppingToken);
            }
        }

        private async Task PerformBackupAsync(ApplicationDbContext db)
        {
            // Thư mục lưu backup
            string backupFolder = Path.Combine(Directory.GetCurrentDirectory(), "Backups");
            if (!Directory.Exists(backupFolder))
            {
                Directory.CreateDirectory(backupFolder);
            }

            string todayBackupFileName = $"FurnitureWms_{DateTime.Now:yyyyMMdd}.bak";
            string backupPath = Path.Combine(backupFolder, todayBackupFileName);

            // Kiểm tra nếu hôm nay đã có file backup rồi thì không backup nữa
            if (File.Exists(backupPath))
            {
                return; // Đã backup trong ngày hôm nay
            }

            _logger.LogInformation("Starting database backup to {BackupPath}", backupPath);

            try
            {
                string sql = $"BACKUP DATABASE [FurnitureWms] TO DISK = '{backupPath}' WITH INIT, NAME = 'FurnitureWms-Full Database Backup'";
                
                // Thực thi lệnh BACKUP
                await db.Database.ExecuteSqlRawAsync(sql);
                
                _logger.LogInformation("Database backup completed successfully.");
            }
            catch (SqlException ex)
            {
                _logger.LogError(ex, "SQL Exception during backup. Ensure SQL Server has write permissions to {BackupPath}.", backupPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to backup database.");
            }
        }
    }
}
