using System.Net.Http.Json;
using System.Text.Json;

namespace DuAnCode.Web.Services
{
    public interface IOllamaClient
    {
        Task<string> GenerateAsync(string prompt);
    }

    public class OllamaClient : IOllamaClient
    {
        private readonly HttpClient _http;
        public OllamaClient(HttpClient http) { _http = http; }

        public async Task<string> GenerateAsync(string prompt)
        {
            var payload = new { model = "llama3", prompt = prompt };
            var resp = await _http.PostAsJsonAsync("/api/generate", payload);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadFromJsonAsync<JsonElement>();
            if (json.ValueKind == JsonValueKind.Object && json.TryGetProperty("text", out var txt)) return txt.GetString() ?? string.Empty;
            return json.ToString();
        }
    }
}
