using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Background
{
    public class AiMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<AiMonitoringService> _logger;
        private readonly TimeSpan _interval = TimeSpan.FromMinutes(30);

        public AiMonitoringService(IServiceProvider sp, ILogger<AiMonitoringService> logger)
        {
            _sp = sp; _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while(!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _sp.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var ollama = scope.ServiceProvider.GetRequiredService<IOllamaClient>();

                    // Fetch data and detect anomalies
                    var since60 = DateTime.UtcNow.AddDays(-60);
                    var since7 = DateTime.UtcNow.AddDays(-7);

                    var movements60 = await db.StockMovements.Where(m => m.CreatedAt >= since60).ToListAsync();
                    var movements7 = await db.StockMovements.Where(m => m.CreatedAt >= since7).ToListAsync();

                    // Simple anomaly detection per SKU by comparing averages
                    var skuIds = movements60.Select(m => m.SkuId).Union(movements7.Select(m => m.SkuId)).Distinct();
                    foreach(var sku in skuIds)
                    {
                        var avg60 = movements60.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        var avg7 = movements7.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        if (avg60 > 0)
                        {
                            var diff = Math.Abs(avg7 - avg60) / (double)avg60;
                            if (diff >= 0.2)
                            {
                                var payload = new { sku = sku, avg60 = avg60, avg7 = avg7, diff = diff };
                                db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "ANOMALY", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                            }
                        }
                    }

                    // Bottleneck detection for combos
                    var combos = await db.ComboProducts.Include(c => c.Components).ToListAsync();
                    foreach(var combo in combos)
                    {
                        // compute available sets
                        int minSets = int.MaxValue; string bottleneckSku = string.Empty;
                        foreach(var comp in combo.Components)
                        {
                            var total = await db.StockLedgers.Where(l => l.SkuId == comp.SkuId && l.Status == "GOOD").SumAsync(l => l.Quantity);
                            var sets = total / Math.Max(1, comp.QuantityPerSet);
                            if (sets < minSets) { minSets = sets; bottleneckSku = comp.SkuId; }
                        }
                        if (minSets < combo.MinStock)
                        {
                            var payload = new { combo = combo.ComboId, minSets = minSets, bottleneckSku = bottleneckSku };
                            db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "BOTTLENECK", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                        }
                    }

                    // Replenishment suggestion: use simple formula per SKU
                    foreach(var sku in skuIds)
                    {
                        var avg60 = movements60.Where(m => m.SkuId == sku).Select(m => Math.Abs(m.QuantityDelta)).DefaultIfEmpty(0).Average();
                        double seasonal = 1.0; // simple placeholder, could be improved
                        var suggested = (int)Math.Ceiling(avg60 * seasonal - await db.StockLedgers.Where(l => l.SkuId == sku && l.Status == "GOOD").SumAsync(l => l.Quantity));
                        if (suggested > 0)
                        {
                            var payload = new { sku = sku, suggested = suggested };
                            db.AiSuggestions.Add(new AiSuggestion { SuggestionType = "REPLENISHMENT", PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload) });
                        }
                    }

                    await db.SaveChangesAsync();
                }
                catch(Exception ex)
                {
                    _logger.LogError(ex, "Error in AiMonitoringService");
                }

                await Task.Delay(_interval, stoppingToken);
            }
        }
    }
}
