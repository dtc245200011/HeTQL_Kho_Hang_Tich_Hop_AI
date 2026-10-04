using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public interface IOllamaClient
    {
        Task<string> GenerateAsync(string prompt);
    }

    public class OllamaClient : IOllamaClient
    {
        private readonly HttpClient _http;
        private readonly IServiceProvider _serviceProvider;

        public OllamaClient(HttpClient http, IServiceProvider serviceProvider) 
        { 
            _http = http; 
            _serviceProvider = serviceProvider;
        }

        public async Task<string> GenerateAsync(string prompt)
        {
            var ollamaUrl = "http://localhost:11434";
            var modelName = "llama3";

            using (var scope = _serviceProvider.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<DuAnCode.Web.Data.ApplicationDbContext>();
                var config = await db.SystemConfigs.FirstOrDefaultAsync();
                if (config != null)
                {
                    if (!string.IsNullOrWhiteSpace(config.OllamaUrl)) ollamaUrl = config.OllamaUrl;
                    if (!string.IsNullOrWhiteSpace(config.OllamaModel)) modelName = config.OllamaModel;
                }
            }

            var requestUrl = $"{ollamaUrl.TrimEnd('/')}/api/generate";
            
            // Ollama standard API returns a stream of JSON objects, but if stream=false is not used, it defaults to true
            var payloadNoStream = new { model = modelName, prompt = prompt, stream = false };
            var resp = await _http.PostAsJsonAsync(requestUrl, payloadNoStream);
            resp.EnsureSuccessStatusCode();

            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("response", out var txt)) return txt.GetString() ?? string.Empty;
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("text", out var txt2)) return txt2.GetString() ?? string.Empty;
            return json.ToString();
        }
    }
}
