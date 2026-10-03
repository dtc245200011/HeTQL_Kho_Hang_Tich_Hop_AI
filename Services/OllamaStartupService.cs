using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace DuAnCode.Web.Services
{
    public class OllamaStartupService : IHostedService
    {
        private readonly ILogger<OllamaStartupService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public OllamaStartupService(ILogger<OllamaStartupService> logger, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Checking if Ollama is running...");
            bool isRunning = false;
            try
            {
                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(2);
                var response = await client.GetAsync("http://localhost:11434/api/version", cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    isRunning = true;
                    _logger.LogInformation("Ollama is already running.");
                }
            }
            catch
            {
                isRunning = false;
            }

            if (!isRunning)
            {
                _logger.LogInformation("Ollama is NOT running. Attempting to start Ollama...");
                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = "ollama",
                        Arguments = "serve",
                        UseShellExecute = true,
                        CreateNoWindow = true
                    };
                    Process.Start(psi);
                    _logger.LogInformation("Ollama started successfully.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to start Ollama process.");
                }
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
