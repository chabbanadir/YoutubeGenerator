using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using YoutubeGenerator.Interfaces;

namespace YoutubeGenerator.crons
{
    public class CronShortsUploadService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<CronShortsUploadService> _logger;

        public CronShortsUploadService(
            IServiceScopeFactory scopeFactory,
            ILogger<CronShortsUploadService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CronShortsUploadService initialized (Cadence: 20-min schedule release).");

            // Initial short wait to let app startup settle
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var scheduler = scope.ServiceProvider.GetRequiredService<IShortsSchedulerService>();

                    // Process pending shorts in queue
                    bool uploaded = await scheduler.ProcessNextScheduledShortAsync();
                    if (uploaded)
                    {
                        // Wait a brief moment before processing next in batch
                        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[CronShorts] Error in shorts processing cycle.");
                }

                // Check for new queued shorts every 1 minute
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }
    }
}
