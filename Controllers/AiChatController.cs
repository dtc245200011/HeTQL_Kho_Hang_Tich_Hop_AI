using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Services;

namespace DuAnCode.Web.Controllers
{
    [Authorize(Roles = "Admin,Director,WarehouseManager,QC,Quản trị viên,Giám đốc,Quản lý kho,Kiểm định chất lượng (QC)")]
    [ApiController]
    [Route("api/[controller]")]
    public class AiChatController : ControllerBase
    {
        private readonly IHttpClientFactory _clientFactory;
        private readonly ApplicationDbContext _db;
        private readonly IAiService _aiService;
        private readonly IAiChatGroundingService _groundingService;

        public AiChatController(
            IHttpClientFactory clientFactory, 
            ApplicationDbContext db, 
            IAiService aiService,
            IAiChatGroundingService groundingService)
        {
            _clientFactory = clientFactory;
            _db = db;
            _aiService = aiService;
            _groundingService = groundingService;
        }

        private async Task<InventoryVoucher> CreateDraftVoucherInternalAsync(string type, string? counterparty, string? skuInput, int quantity, string? customReason = null)
        {
            type = (type ?? "INBOUND").ToUpperInvariant().Trim();
            if (type != "OUTBOUND") type = "INBOUND";

            var prefix = type == "OUTBOUND" ? "PX-" : "PN-";
            var voucherNum = prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss");

            var warehouse = await _db.Warehouses.FirstOrDefaultAsync();
            var warehouseId = warehouse?.WarehouseId ?? "WH-01";

            SkuVariant? sku = null;
            if (!string.IsNullOrWhiteSpace(skuInput))
            {
                var cleanSku = skuInput.Trim();
                sku = await _db.SkuVariants.FirstOrDefaultAsync(s => s.SkuId.ToLower() == cleanSku.ToLower());
            }
            if (sku == null)
            {
                sku = await _db.SkuVariants.FirstOrDefaultAsync();
            }

            var finalSkuId = sku?.SkuId ?? "SKU-001";
            var pm = sku != null ? await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == sku.ProductModelId) : null;
            var prodName = pm?.ProductName ?? finalSkuId;
            var unitPrice = sku?.UnitPrice > 0 ? sku.UnitPrice : 1500000m;
            var qty = quantity > 0 ? quantity : 10;

            var defaultPartner = type == "OUTBOUND" ? "Khách hàng mua lẻ" : "Công ty TNHH Nội Thất An Phát";
            var finalCounterparty = string.IsNullOrWhiteSpace(counterparty) ? defaultPartner : counterparty.Trim();

            var username = User?.Identity?.Name ?? "admin";
            var creatorName = "Trợ lý AI" + (!string.IsNullOrWhiteSpace(username) ? $" ({username})" : "");

            var voucher = new InventoryVoucher
            {
                VoucherId = Guid.NewGuid().ToString(),
                VoucherNumber = voucherNum,
                Date = DateTime.UtcNow,
                Type = type,
                Counterparty = finalCounterparty,
                WarehouseId = warehouseId,
                Status = "DRAFT",
                TotalAmount = unitPrice * qty,
                CreatedBy = creatorName,
                Address = "Hệ thống WMS",
                ReferenceDocument = "PO-AI-" + DateTime.UtcNow.ToString("yyMMddHHmm"),
                Reason = string.IsNullOrWhiteSpace(customReason)
                    ? $"Tạo tự động từ Trợ lý AI WMS theo yêu cầu của {username}"
                    : customReason
            };

            var line = new VoucherLine
            {
                VoucherId = voucher.VoucherId,
                SkuId = finalSkuId,
                SkuCode = finalSkuId,
                ProductName = prodName,
                Unit = pm?.Unit ?? "Cái",
                Quantity = qty,
                DocumentQuantity = qty,
                UnitPrice = unitPrice,
                LineTotal = unitPrice * qty,
                BatchNumber = "BATCH-" + DateTime.UtcNow.ToString("yyMMdd")
            };

            voucher.Lines.Add(line);
            _db.InventoryVouchers.Add(voucher);
            await _db.SaveChangesAsync();

            return voucher;
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
                client.Timeout = TimeSpan.FromSeconds(120);

                // Fetch dynamic grounding context from database
                var warehouseContext = await _groundingService.BuildContextPromptAsync(request.Prompt);

                var systemPrompt = $@"Bạn là trợ lý AI quản lý kho hàng của hệ thống DuAnCode WMS.
{warehouseContext}

=== HƯỚNG DẪN QUY TRÌNH HỆ THỐNG ===
1. Nhập kho (/Inbound): Lập phiếu -> Kiểm định QC -> Hoàn tất nhập.
2. Xuất kho (/Outbound): Lập phiếu xuất -> Phê duyệt -> Xuất kho.
3. Chuyển kho (/StockTransfer): Điều chuyển giữa các kho/vị trí -> Phê duyệt -> Cập nhật.
4. Kiểm kê (/InventoryAudit): Đếm hàng thực tế -> Cân chỉnh chênh lệch.
5. Phân tích AI (/Replenishment): Quét biến động tồn kho 7 ngày vs 60 ngày, phát hiện tắc nghẽn cổ chai combo, dự báo nhu cầu và đề xuất nhập bù.

=== NGUYÊN TẮC TRẢ LỜI & MÃ LỆNH (CỰC KỲ QUAN TRỌNG) ===
- Dựa CHÍNH XÁC vào dữ liệu thực tế ở trên để trả lời về tồn kho, mặt hàng, cảnh báo. Tuyệt đối KHÔNG tự bịa số liệu tồn kho.
- TUYỆT ĐỐI KHÔNG TỰ BỊA MÃ PHIẾU (như PN-xxxx, PX-xxxx, PN-2026..., PX-2026...) trong câu trả lời văn bản. Hệ thống máy chủ backend là nơi duy nhất có thẩm quyền sinh mã phiếu thực tế và lưu vào cơ sở dữ liệu.
- Khi người dùng yêu cầu: 'tạo đơn mới', 'tạo một đơn mới', 'tạo phiếu mới', 'tạo phiếu nhập', 'lập phiếu nhập', 'tạo đơn nhập hàng', 'lập đơn hàng', 'nhập hàng mới', 'nhập kho':
  + Trích xuất thông tin: đối tác, sku, số lượng (nếu người dùng không cung cấp, dùng SKU và đối tác có trong dữ liệu kho).
  + BẮT BUỘC thêm mã lệnh ở cuối: [ACTION: CREATE_DRAFT_INBOUND: {{""counterparty"": ""<tên đối tác/NCC>"", ""sku"": ""<mã SKU>"", ""quantity"": <số lượng>}}]
- Nếu người dùng yêu cầu tạo phiếu xuất kho / bán hàng / xuất hàng:
  + BẮT BUỘC thêm mã lệnh ở cuối: [ACTION: CREATE_DRAFT_OUTBOUND: {{""counterparty"": ""<tên khách hàng>"", ""sku"": ""<mã SKU>"", ""quantity"": <số lượng>}}]
- Nếu người dùng yêu cầu 'mở', 'đi đến', 'truy cập' chức năng (vd: phiếu nhập, xuất kho, chuyển kho, kiểm kê, báo cáo, đề xuất nhập, sao lưu, quản trị người dùng, cấu hình hệ thống), thêm lệnh ở cuối: [NAVIGATE: <url>] (URL hợp lệ: /Inbound, /Outbound, /StockTransfer, /InventoryAudit, /Replenishment, /Report, /Admin/Backup, /Admin/Users, /Admin/Settings, /Admin/Logs, /Dashboard, /AiDashboard).
- Nếu người dùng yêu cầu 'tạo báo cáo', 'xuất báo cáo', 'báo cáo tồn kho': thêm lệnh: [NAVIGATE: /Report]
- Nếu người dùng yêu cầu 'tạo backup', 'tạo bản backup', 'sao lưu dữ liệu', 'sao lưu hệ thống', 'backup dữ liệu', 'tạo bản sao lưu': thêm lệnh: [ACTION: TRIGGER_BACKUP]
- Nếu người dùng yêu cầu 'mở trang sao lưu', 'quản lý backup', 'xem bản sao lưu', 'khôi phục dữ liệu': thêm lệnh: [NAVIGATE: /Admin/Backup]
- Nếu người dùng yêu cầu 'chạy phân tích AI', 'tạo gợi ý nhập hàng', 'phân tích kho', thêm lệnh: [ACTION: TRIGGER_REPLENISHMENT]
- Trả lời thân thiện, ngắn gọn, dùng gạch đầu dòng rõ ràng bằng tiếng Việt. Tuyệt đối KHÔNG kết thúc câu lơ lửng bằng 'ở đây:' hay 'tại đây:'.";

                var messagesList = new List<object>
                {
                    new { role = "system", content = systemPrompt }
                };

                // Append multi-turn history if provided (up to last 6 messages)
                if (request.History != null && request.History.Count > 0)
                {
                    var recentHistory = request.History
                        .Where(h => !string.IsNullOrWhiteSpace(h.Content))
                        .TakeLast(6);

                    foreach (var h in recentHistory)
                    {
                        var role = (h.Role?.ToLower() == "ai" || h.Role?.ToLower() == "assistant") ? "assistant" : "user";
                        messagesList.Add(new { role = role, content = h.Content });
                    }
                }

                // Add current prompt
                messagesList.Add(new { role = "user", content = request.Prompt.Trim() });

                var payload = new
                {
                    model = modelName,
                    messages = messagesList,
                    stream = false
                };

                var requestUrl = $"{ollamaUrl.TrimEnd('/')}/api/chat";
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(requestUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var resultStr = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(resultStr);
                    var reply = doc.RootElement.GetProperty("message").GetProperty("content").GetString() ?? "";

                    // 1. Process ACTION CREATE_DRAFT if emitted by LLM
                    var actionMatch = Regex.Match(reply, @"\[ACTION:\s*CREATE_DRAFT_(INBOUND|OUTBOUND):\s*(\{.*?\})\]", RegexOptions.Singleline);
                    if (actionMatch.Success)
                    {
                        var actType = actionMatch.Groups[1].Value.ToUpperInvariant();
                        var jsonStr = actionMatch.Groups[2].Value;
                        string counterparty = "";
                        string sku = "";
                        int quantity = 10;
                        try
                        {
                            using var actDoc = JsonDocument.Parse(jsonStr);
                            if (actDoc.RootElement.TryGetProperty("counterparty", out var cp)) counterparty = cp.GetString() ?? "";
                            if (actDoc.RootElement.TryGetProperty("sku", out var sk)) sku = sk.GetString() ?? "";
                            if (actDoc.RootElement.TryGetProperty("quantity", out var q)) quantity = q.GetInt32();
                        }
                        catch { }

                        var voucher = await CreateDraftVoucherInternalAsync(actType, counterparty, sku, quantity);
                        var firstLine = voucher.Lines.FirstOrDefault();
                        var targetUrl = (voucher.Type == "OUTBOUND" ? "/Outbound" : "/Inbound") + $"?search={voucher.VoucherNumber}";
                        var directUrl = (voucher.Type == "OUTBOUND" ? "/Outbound/Create" : "/Inbound/Create") + $"?id={voucher.VoucherId}";
                        
                        var successNotice = $"\n\n✅ **Đã tạo thành công Bản nháp Phiếu {(voucher.Type == "OUTBOUND" ? "Xuất" : "Nhập")} kho vào CSDL hệ thống!**\n" +
                                            $"- **Mã phiếu:** `{voucher.VoucherNumber}`\n" +
                                            $"- **Trạng thái:** `Bản nháp (DRAFT)`\n" +
                                            $"- **Người tạo:** `{voucher.CreatedBy}`\n" +
                                            $"- **Đối tác:** {voucher.Counterparty}\n" +
                                            $"- **Mặt hàng:** {firstLine?.ProductName} (`{firstLine?.SkuId}`) — Số lượng: {firstLine?.Quantity} {firstLine?.Unit}\n" +
                                            $"- **Tổng giá trị dự kiến:** {voucher.TotalAmount:N0} đ\n\n" +
                                            $"👉 [Mở & Sửa phiếu {voucher.VoucherNumber}]({directUrl}) • [Xem danh sách phiếu]({targetUrl})";

                        reply = reply.Replace(actionMatch.Value, successNotice);
                    }
                    else
                    {
                        // 2. Fallback: If user prompt asked to create order/voucher, but LLM forgot action tag or hallucinated code
                        var lowerPrompt = request.Prompt.ToLower().Trim();
                        bool isCreateIntent = Regex.IsMatch(lowerPrompt, @"(tạo|lập|thêm|khởi tạo)\s+(một\s+)?(đơn|phiếu|bản nháp)")
                            || Regex.IsMatch(lowerPrompt, @"(tạo đơn mới|tạo phiếu mới|tạo đơn nhập|tạo phiếu nhập|tạo đơn xuất|tạo phiếu xuất|nhập hàng mới|lập đơn hàng|tạo đơn hàng)");

                        if (isCreateIntent)
                        {
                            string intentType = lowerPrompt.Contains("xuất") ? "OUTBOUND" : "INBOUND";
                            
                            var qtyMatch = Regex.Match(lowerPrompt, @"(\d+)\s*(cái|chiếc|bộ|hộp|sp|sản phẩm|đơn vị)?");
                            int extractedQty = qtyMatch.Success && int.TryParse(qtyMatch.Groups[1].Value, out var qVal) && qVal > 0 ? qVal : 10;
                            
                            var skuMatch = Regex.Match(request.Prompt, @"(SKU-[A-Za-z0-9_-]+)", RegexOptions.IgnoreCase);
                            string? extractedSku = skuMatch.Success ? skuMatch.Groups[1].Value : null;

                            var voucher = await CreateDraftVoucherInternalAsync(intentType, null, extractedSku, extractedQty);
                            var firstLine = voucher.Lines.FirstOrDefault();
                            var targetUrl = (voucher.Type == "OUTBOUND" ? "/Outbound" : "/Inbound") + $"?search={voucher.VoucherNumber}";
                            var directUrl = (voucher.Type == "OUTBOUND" ? "/Outbound/Create" : "/Inbound/Create") + $"?id={voucher.VoucherId}";

                            // If LLM fabricated a fake voucher number like PN-20261029123456 or PN-xxxx, replace it with the REAL voucher number!
                            reply = Regex.Replace(reply, @"(PN|PX)-[0-9A-Za-z_-]{6,}", voucher.VoucherNumber);

                            if (!reply.Contains(voucher.VoucherNumber))
                            {
                                reply += $"\n\n✅ **Đã tạo thành công Bản nháp Phiếu {(voucher.Type == "OUTBOUND" ? "Xuất" : "Nhập")} kho vào CSDL hệ thống!**\n" +
                                         $"- **Mã phiếu:** `{voucher.VoucherNumber}`\n" +
                                         $"- **Trạng thái:** `Bản nháp (DRAFT)`\n" +
                                         $"- **Người tạo:** `{voucher.CreatedBy}`\n" +
                                         $"- **Đối tác:** {voucher.Counterparty}\n" +
                                         $"- **Mặt hàng:** {firstLine?.ProductName} (`{firstLine?.SkuId}`) — Số lượng: {firstLine?.Quantity} {firstLine?.Unit}\n" +
                                         $"- **Tổng giá trị dự kiến:** {voucher.TotalAmount:N0} đ\n\n" +
                                         $"👉 [Mở & Sửa phiếu {voucher.VoucherNumber}]({directUrl}) • [Xem danh sách phiếu]({targetUrl})";
                            }
                            else
                            {
                                reply += $"\n\n👉 [Mở & Sửa phiếu {voucher.VoucherNumber}]({directUrl}) • [Xem danh sách phiếu]({targetUrl})";
                            }
                        }
                    }

                    // 3. Process Backup creation intent if requested
                    var lowerPromptForBackup = request.Prompt.ToLower().Trim();
                    bool isBackupIntent = Regex.IsMatch(reply, @"\[ACTION:\s*TRIGGER_BACKUP\]", RegexOptions.IgnoreCase)
                        || Regex.IsMatch(lowerPromptForBackup, @"(tạo|làm|chạy|khởi tạo)\s+(cho tôi\s+)?(một\s+)?(bản\s+)?(backup|sao lưu)")
                        || ((lowerPromptForBackup.Contains("backup") || lowerPromptForBackup.Contains("sao lưu")) && (lowerPromptForBackup.Contains("tạo") || lowerPromptForBackup.Contains("thời điểm hiện tại") || lowerPromptForBackup.Contains("ngay")));

                    if (isBackupIntent)
                    {
                        bool isAdmin = User.IsInRole("Admin") || User.IsInRole("Director") || User.IsInRole("Quản trị viên") || User.IsInRole("Giám đốc");
                        if (!isAdmin)
                        {
                            var noPermMsg = "\n\n⚠️ **Quyền truy cập hạn chế:** Bạn hiện đang đăng nhập bằng tài khoản không có quyền Quản trị viên (Admin/Director). Thao tác tạo hoặc tải bản sao lưu dữ liệu toàn hệ thống yêu cầu đặc quyền Quản trị viên. Vui lòng liên hệ Admin để được hỗ trợ.";
                            reply = Regex.Replace(reply, @"\[ACTION:\s*TRIGGER_BACKUP\]", "", RegexOptions.IgnoreCase).Trim();
                            reply = Regex.Replace(reply, @"\[NAVIGATE:\s*[^\]]+\]", "", RegexOptions.IgnoreCase).Trim();
                            reply += noPermMsg;
                        }
                        else
                        {
                            try
                            {
                                var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
                                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                                var fileName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                                var path = Path.Combine(dir, fileName);

                                var export = await AdminController.BuildExportDataAsync(_db);
                                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                                var json = System.Text.Json.JsonSerializer.Serialize(export, jsonOptions);
                                await System.IO.File.WriteAllTextAsync(path, json);

                                var fileInfo = new FileInfo(path);
                                var sizeKb = fileInfo.Length / 1024.0;
                                var downloadUrl = $"/Admin/DownloadBackup?name={fileName}";
                                var backupNotice = $"\n\n💾 **Đã khởi tạo bản sao lưu toàn hệ thống thành công!**\n" +
                                                   $"- **Tệp sao lưu:** `{fileName}`\n" +
                                                   $"- **Dung lượng:** `{sizeKb:F1} KB`\n" +
                                                   $"- **Thời điểm snapshot:** `{DateTime.Now:dd/MM/yyyy HH:mm:ss}`\n" +
                                                   $"- **Phạm vi:** Toàn bộ CSDL (Tồn kho, Phiếu nhập/xuất, Mặt hàng, Đối tác, Phân quyền)\n\n" +
                                                   $"👉 [Tải bản sao lưu về máy ({fileName})]({downloadUrl}) • [Trang quản lý Sao lưu & Phục hồi](/Admin/Backup)";

                                reply = Regex.Replace(reply, @"\[ACTION:\s*TRIGGER_BACKUP\]", "", RegexOptions.IgnoreCase).Trim();
                                reply = Regex.Replace(reply, @"\[NAVIGATE:\s*/Admin/ExportData\]", "", RegexOptions.IgnoreCase).Trim();
                                reply = Regex.Replace(reply, @"\[NAVIGATE:\s*/Admin/Backup\]", "", RegexOptions.IgnoreCase).Trim();
                                reply += backupNotice;
                            }
                            catch (Exception ex)
                            {
                                reply += $"\n\n❌ **Lỗi khi tạo bản sao lưu:** {ex.Message}. Vui lòng thử lại tại [Trang Quản lý Sao lưu](/Admin/Backup).";
                            }
                        }
                    }

                    // 4. Sanitize any outdated /Admin/ExportData links or navigate commands
                    reply = reply.Replace("/Admin/ExportData", "/Admin/Backup");

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

        [HttpPost("trigger-backup")]
        [Authorize(Roles = "Admin,Director,Quản trị viên,Giám đốc")]
        public async Task<IActionResult> TriggerBackup()
        {
            try
            {
                var dir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "backups");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var fileName = $"backup_{DateTime.UtcNow:yyyyMMddHHmmss}.json";
                var path = Path.Combine(dir, fileName);

                var export = await AdminController.BuildExportDataAsync(_db);
                var jsonOptions = new System.Text.Json.JsonSerializerOptions { WriteIndented = true, ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles };
                var json = System.Text.Json.JsonSerializer.Serialize(export, jsonOptions);
                await System.IO.File.WriteAllTextAsync(path, json);

                return Ok(new { success = true, file = fileName, message = $"Đã tạo bản sao lưu '{fileName}' thành công!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi tạo bản sao lưu: " + ex.Message });
            }
        }

        [HttpPost("trigger-replenishment")]
        [Authorize(Roles = "Admin,Director,WarehouseManager,Quản trị viên,Giám đốc,Quản lý kho")]
        public async Task<IActionResult> TriggerReplenishment()
        {
            try
            {
                await _aiService.RunAnalysisAsync();
                return Ok(new { success = true, message = "Đã quét phân tích dữ liệu kho thành công và cập nhật các đề xuất mới!" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Lỗi khi chạy quét phân tích AI: " + ex.Message });
            }
        }

        [HttpPost("create-draft-voucher")]
        [Authorize(Roles = "Admin,Director,WarehouseManager,Quản trị viên,Giám đốc,Quản lý kho")]
        public async Task<IActionResult> CreateDraftVoucher([FromBody] CreateDraftVoucherRequest request)
        {
            var voucher = await CreateDraftVoucherInternalAsync(request.Type, request.Counterparty, request.Sku, request.Quantity);
            var firstLine = voucher.Lines.FirstOrDefault();

            return Ok(new
            {
                success = true,
                voucherId = voucher.VoucherId,
                voucherNumber = voucher.VoucherNumber,
                type = voucher.Type,
                counterparty = voucher.Counterparty,
                sku = firstLine?.SkuId,
                productName = firstLine?.ProductName,
                quantity = firstLine?.Quantity,
                totalAmount = voucher.TotalAmount,
                directUrl = (voucher.Type == "OUTBOUND" ? "/Outbound/Create" : "/Inbound/Create") + $"?id={voucher.VoucherId}",
                targetUrl = (voucher.Type == "OUTBOUND" ? "/Outbound" : "/Inbound") + $"?search={voucher.VoucherNumber}",
                message = $"Đã tạo thành công bản nháp phiếu {(voucher.Type == "OUTBOUND" ? "xuất" : "nhập")} kho {voucher.VoucherNumber}!"
            });
        }
    }

    public class CreateDraftVoucherRequest
    {
        public string Type { get; set; } = "INBOUND"; // INBOUND or OUTBOUND
        public string? Counterparty { get; set; }
        public string? Sku { get; set; }
        public int Quantity { get; set; } = 1;
    }

    public class ChatMessageItem
    {
        public string Role { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
    }

    public class ChatRequest
    {
        public string Prompt { get; set; } = string.Empty;
        public List<ChatMessageItem>? History { get; set; }
    }
}
