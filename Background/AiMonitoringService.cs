using DuAnCode.Web.Data;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Background
{
    public class AiMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<AiMonitoringService> _logger;

        public AiMonitoringService(IServiceProvider sp, ILogger<AiMonitoringService> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var aiService = scope.ServiceProvider.GetRequiredService<IAiService>();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    await aiService.RunAnalysisAsync(stoppingToken);

                    var config = await db.SystemConfigs.FirstOrDefaultAsync(stoppingToken);
                    var currentIntervalMinutes = config?.AiScanIntervalMinutes > 0 ? config.AiScanIntervalMinutes : 30;

                    await Task.Delay(TimeSpan.FromMinutes(currentIntervalMinutes), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Lỗi xảy ra trong tiến trình nền AiMonitoringService");
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
        }
    }
}
