using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using DuAnCode.Web.Data;
using DuAnCode.Web.Models;

namespace DuAnCode.Web.Controllers
{
    [Authorize(Roles = "Admin,Director,WarehouseManager,QC")]
    [ApiController]
    [Route("api/[controller]")]
    public class AiChatController : ControllerBase
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ApplicationDbContext _db;

        public AiChatController(IHttpClientFactory clientFactory, ApplicationDbContext db)
        {
            _clientFactory = clientFactory;
            _db = db;
        }

        [HttpPost("chat")]
        public async Task<IActionResult> Chat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Prompt)) return BadRequest("Prompt cannot be empty.");

            try
            {
                var config = await _db.SystemConfigs.FirstOrDefaultAsync();
                var ollamaUrl = string.IsNullOrWhiteSpace(config?.OllamaUrl) ? "http://localhost:11434" : config.OllamaUrl;
                var modelName = string.IsNullOrWhiteSpace(config?.OllamaModel) ? "qwen2.5-coder:7b" : config.OllamaModel;

                var client = _clientFactory.CreateClient();
                var payload = new
                {
                    model = modelName,
                    messages = new[] {
                        new { role = "system", content = "Bạn là trợ lý AI quản lý kho hàng của hệ thống DuAnCode WMS. Đặc biệt lưu ý các LỆNH sau:\n- Nếu người dùng yêu cầu 'mở', 'đi đến', 'truy cập' chức năng (vd: phiếu nhập, xuất kho, kiểm kê, báo cáo), thêm lệnh ở cuối: [NAVIGATE: <url>] (URL: /Inbound, /Outbound, /StockTransfer, /InventoryAudit, /Replenishment, /Report, /Admin/Users).\n- Nếu người dùng yêu cầu 'tạo báo cáo', 'xuất báo cáo', 'xuất dữ liệu', thêm lệnh: [NAVIGATE: /Admin/ExportData]\n- Nếu người dùng yêu cầu 'chạy phân tích AI', 'tạo gợi ý nhập hàng', 'phân tích kho', thêm lệnh: [ACTION: TRIGGER_REPLENISHMENT]\nHãy trả lời thân thiện và kèm theo mã lệnh tương ứng." },
                        new { role = "user", content = request.Prompt }
                    },
                    stream = false
                };

                var requestUrl = $"{ollamaUrl.TrimEnd('/')}/api/chat";
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var resultStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(resultStr);
                    var reply = doc.RootElement.GetProperty("message").GetProperty("content").GetString();
                    return Ok(new { reply });
                }
                var errorBody = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, $"Lỗi từ Ollama: {errorBody}");
            }
            catch (Exception ex)
            {
                return StatusCode(500, "Lỗi kết nối Ollama: " + ex.Message);
            }
        }

        [HttpPost("trigger-replenishment")]
        public async Task<IActionResult> TriggerReplenishment()
        {
            var sku = await _db.SkuVariants.FirstOrDefaultAsync();
            var skuId = sku != null ? sku.SkuId : "SKU-001";
            var payload = new { sku = skuId, suggested = 100, diff = 0.5, avg7 = 50 };
            
            var suggestion = new AiSuggestion
            {
                SuggestionId = Guid.NewGuid().ToString(),
                SuggestionType = "REPLENISHMENT",
                SkuId = skuId,
                PayloadJson = JsonSerializer.Serialize(payload),
                CreatedAt = DateTime.UtcNow,
                Status = "PENDING_REVIEW"
            };
            _db.AiSuggestions.Add(suggestion);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
    }

    public class ChatRequest
    {
        public string Prompt { get; set; } = string.Empty;
    }
}
