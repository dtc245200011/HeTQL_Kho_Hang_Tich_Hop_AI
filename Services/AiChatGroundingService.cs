using System.Text;
using System.Text.Json;
using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public interface IAiChatGroundingService
    {
        Task<string> BuildContextPromptAsync(string userPrompt, CancellationToken cancellationToken = default);
    }

    public class AiChatGroundingService : IAiChatGroundingService
    {
        private readonly ApplicationDbContext _db;

        public AiChatGroundingService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<string> BuildContextPromptAsync(string userPrompt, CancellationToken cancellationToken = default)
        {
            var sb = new StringBuilder();
            var lowerPrompt = (userPrompt ?? "").ToLower();

            try
            {
                // 1. Snapshot tồn kho tổng quan
                var totalSkuCount = await _db.SkuVariants.CountAsync(cancellationToken);
                var totalGoodStock = await _db.StockLedgers
                    .Where(l => l.Status == "GOOD")
                    .SumAsync(l => (int?)l.Quantity, cancellationToken) ?? 0;
                var totalDamagedStock = await _db.StockLedgers
                    .Where(l => l.Status == "DAMAGED")
                    .SumAsync(l => (int?)l.Quantity, cancellationToken) ?? 0;
                var pendingAiSuggestionsCount = await _db.AiSuggestions
                    .CountAsync(s => s.Status == "PENDING_REVIEW", cancellationToken);

                // 2. Thống kê chi tiết TOÀN BỘ chứng từ xuất/nhập/chuyển kho theo Loại và Trạng thái
                var voucherCounts = await _db.InventoryVouchers.AsNoTracking()
                    .GroupBy(v => new { v.Type, v.Status })
                    .Select(g => new { g.Key.Type, g.Key.Status, Count = g.Count() })
                    .ToListAsync(cancellationToken);

                int GetCount(string type, string status) =>
                    voucherCounts.FirstOrDefault(x => x.Type == type && x.Status == status)?.Count ?? 0;

                int inApproved = GetCount("INBOUND", "APPROVED");
                int inRejected = GetCount("INBOUND", "REJECTED");
                int inPending = GetCount("INBOUND", "PENDING");
                int inDraft = GetCount("INBOUND", "DRAFT");
                int inTotal = inApproved + inRejected + inPending + inDraft;

                int outApproved = GetCount("OUTBOUND", "APPROVED");
                int outRejected = GetCount("OUTBOUND", "REJECTED");
                int outPending = GetCount("OUTBOUND", "PENDING");
                int outDraft = GetCount("OUTBOUND", "DRAFT");
                int outTotal = outApproved + outRejected + outPending + outDraft;

                int transferRejected = GetCount("TRANSFER", "REJECTED");

                sb.AppendLine("=== DỮ LIỆU THỰC TẾ TRÍCH XUẤT TỪ CƠ SỞ DỮ LIỆU HỆ THỐNG WMS ===");
                sb.AppendLine($"- Tổng số mã SKU hiện có: {totalSkuCount}");
                sb.AppendLine($"- Tổng tồn kho khả dụng (Hàng đạt chuẩn - GOOD): {totalGoodStock} đơn vị");
                sb.AppendLine($"- Tổng tồn kho hàng lỗi/hỏng (DAMAGED): {totalDamagedStock} đơn vị");
                sb.AppendLine($"- Đề xuất phân tích AI chờ xem xét: {pendingAiSuggestionsCount} đề xuất");

                sb.AppendLine("\n* BÁO CÁO TOÀN BỘ PHIẾU XUẤT / NHẬP THEO TỪNG TRẠNG THÁI:");
                sb.AppendLine($"1. Phiếu Nhập kho (INBOUND): Tổng {inTotal} phiếu");
                sb.AppendLine($"   + BỊ TỪ CHỐI DUYỆT (REJECTED): {inRejected} phiếu");
                sb.AppendLine($"   + Đã phê duyệt/Hoàn tất (APPROVED): {inApproved} phiếu");
                sb.AppendLine($"   + Đang chờ duyệt (PENDING): {inPending} phiếu");
                sb.AppendLine($"   + Bản nháp (DRAFT): {inDraft} phiếu");

                sb.AppendLine($"2. Phiếu Xuất kho (OUTBOUND): Tổng {outTotal} phiếu");
                sb.AppendLine($"   + BỊ TỪ CHỐI DUYỆT (REJECTED): {outRejected} phiếu");
                sb.AppendLine($"   + Đã phê duyệt/Hoàn tất (APPROVED): {outApproved} phiếu");
                sb.AppendLine($"   + Đang chờ duyệt (PENDING): {outPending} phiếu");
                sb.AppendLine($"   + Bản nháp (DRAFT): {outDraft} phiếu");

                if (transferRejected > 0)
                {
                    sb.AppendLine($"3. Phiếu Điều chuyển kho (TRANSFER): Có {transferRejected} phiếu bị từ chối duyệt (REJECTED).");
                }

                // 3. Tra cứu chi tiết phiếu theo câu hỏi của người dùng (Intent-aware)
                bool isAskingRecent = lowerPrompt.Contains("vừa tạo") 
                    || lowerPrompt.Contains("mới tạo") 
                    || lowerPrompt.Contains("mã phiếu") 
                    || lowerPrompt.Contains("phiếu đâu") 
                    || lowerPrompt.Contains("phiếu bạn vừa")
                    || lowerPrompt.Contains("phiếu vừa")
                    || lowerPrompt.Contains("tìm lại không thấy")
                    || lowerPrompt.Contains("không thấy")
                    || lowerPrompt.Contains("ở đâu");

                bool isAskingRejected = lowerPrompt.Contains("từ chối") || lowerPrompt.Contains("reject") || lowerPrompt.Contains("bị từ chối") || lowerPrompt.Contains("hủy");
                bool isAskingApproved = lowerPrompt.Contains("đã duyệt") || lowerPrompt.Contains("approved") || lowerPrompt.Contains("hoàn tất");
                bool isAskingPending = lowerPrompt.Contains("chờ duyệt") || lowerPrompt.Contains("pending") || lowerPrompt.Contains("nháp") || lowerPrompt.Contains("draft");

                if (isAskingRecent)
                {
                    var latestVouchers = await _db.InventoryVouchers.AsNoTracking()
                        .OrderByDescending(v => v.Date)
                        .Take(6)
                        .ToListAsync(cancellationToken);
                    if (latestVouchers.Count > 0)
                    {
                        sb.AppendLine("\n* DANH SÁCH CÁC PHIẾU GẦN NHẤT ĐANG CÓ THỰC TẾ TRONG HỆ THỐNG:");
                        foreach (var v in latestVouchers)
                        {
                            sb.AppendLine($"  + Mã phiếu: {v.VoucherNumber} | Loại: {v.Type} | Trạng thái: {v.Status} | Đối tác: {v.Counterparty} | Ngày tạo: {v.Date:dd/MM/yyyy HH:mm} | Người tạo: {v.CreatedBy}");
                        }
                        sb.AppendLine("[QUY TẮC BẮT BUỘC]: Nếu người dùng hỏi mã phiếu vừa tạo, phiếu ở đâu hoặc tìm không thấy, bạn PHẢI CHỈ RÕ mã phiếu thực tế từ danh sách trên (ví dụ phiếu mới nhất là: " + latestVouchers.First().VoucherNumber + "). TUYỆT ĐỐI KHÔNG tự bịa ra mã phiếu không có trong danh sách trên.");
                    }
                }
                else if (isAskingRejected)
                {
                    var targetType = lowerPrompt.Contains("xuất") ? "OUTBOUND" : (lowerPrompt.Contains("nhập") ? "INBOUND" : null);
                    var q = _db.InventoryVouchers.AsNoTracking().Where(v => v.Status == "REJECTED");
                    if (!string.IsNullOrEmpty(targetType)) q = q.Where(v => v.Type == targetType);

                    var recentRejected = await q.OrderByDescending(v => v.Date).Take(6).ToListAsync(cancellationToken);
                    if (recentRejected.Count > 0)
                    {
                        sb.AppendLine("\n* DANH SÁCH CHI TIẾT CÁC PHIẾU BỊ TỪ CHỐI GẦN ĐÂY:");
                        foreach (var v in recentRejected)
                        {
                            var reasonTxt = string.IsNullOrWhiteSpace(v.Reason) ? "Không ghi lý do" : v.Reason;
                            sb.AppendLine($"  + Mã phiếu: {v.VoucherNumber} | Loại: {v.Type} | Đối tác: {v.Counterparty} | Ngày: {v.Date:dd/MM/yyyy HH:mm} | Lý do: {reasonTxt}");
                        }
                    }
                }
                else if (isAskingApproved)
                {
                    var recentApproved = await _db.InventoryVouchers.AsNoTracking()
                        .Where(v => v.Status == "APPROVED")
                        .OrderByDescending(v => v.Date)
                        .Take(5)
                        .ToListAsync(cancellationToken);
                    if (recentApproved.Count > 0)
                    {
                        sb.AppendLine("\n* DANH SÁCH CÁC PHIẾU ĐÃ PHÊ DUYỆT GẦN ĐÂY:");
                        foreach (var v in recentApproved)
                        {
                            sb.AppendLine($"  + Mã phiếu: {v.VoucherNumber} | Loại: {v.Type} | Đối tác: {v.Counterparty} | Ngày: {v.Date:dd/MM/yyyy} | Giá trị: {v.TotalAmount:N0} đ");
                        }
                    }
                }
                else
                {
                    var recentPending = await _db.InventoryVouchers.AsNoTracking()
                        .Where(v => v.Status == "PENDING" || v.Status == "DRAFT")
                        .OrderByDescending(v => v.Date)
                        .Take(5)
                        .ToListAsync(cancellationToken);
                    if (recentPending.Count > 0)
                    {
                        sb.AppendLine("\n* CÁC PHIẾU ĐANG CHỜ DUYỆT / BẢN NHÁP GẦN ĐÂY:");
                        foreach (var v in recentPending)
                        {
                            sb.AppendLine($"  + Mã phiếu: {v.VoucherNumber} | Loại: {v.Type} | Trạng thái: {v.Status} | Đối tác: {v.Counterparty} | Ngày: {v.Date:dd/MM/yyyy}");
                        }
                    }
                }

                // 4. Tra cứu tồn kho các mặt hàng (Stock per SKU)
                var pms = await _db.ProductModels.AsNoTracking().ToDictionaryAsync(p => p.ProductModelId, cancellationToken);
                var allLedgers = await _db.StockLedgers.AsNoTracking()
                    .Where(l => l.Status == "GOOD")
                    .GroupBy(l => l.SkuId)
                    .Select(g => new { SkuId = g.Key, Qty = g.Sum(x => x.Quantity) })
                    .ToListAsync(cancellationToken);
                var stockMap = allLedgers.ToDictionary(x => x.SkuId, x => x.Qty);

                var skus = await _db.SkuVariants.AsNoTracking().Take(30).ToListAsync(cancellationToken);
                
                var lowStockList = new List<string>();
                var regularStockList = new List<string>();

                foreach (var s in skus)
                {
                    pms.TryGetValue(s.ProductModelId, out var pm);
                    var name = pm?.ProductName ?? s.SkuId;
                    var min = pm?.MinStock ?? 0;
                    var qty = stockMap.ContainsKey(s.SkuId) ? stockMap[s.SkuId] : 0;
                    
                    var itemInfo = $"[{s.SkuId}] {name} - Tồn: {qty} (Định mức tối thiểu: {min})";
                    if (min > 0 && qty < min)
                    {
                        lowStockList.Add(itemInfo + " => CẢNH BÁO TỒN THẤP");
                    }
                    else
                    {
                        regularStockList.Add(itemInfo);
                    }
                }

                if (lowStockList.Count > 0)
                {
                    sb.AppendLine("\n* CÁC MẶT HÀNG TỒN KHO THẤP DƯỚI ĐỊNH MỨC AN TOÀN:");
                    foreach (var l in lowStockList.Take(6)) sb.AppendLine("  + " + l);
                }

                sb.AppendLine("\n* DANH SÁCH MẶT HÀNG TRỌNG ĐIỂM TRONG KHO:");
                foreach (var r in regularStockList.Take(10)) sb.AppendLine("  + " + r);

                // 5. Cảnh báo phân tích AI gần nhất
                var recentAi = await _db.AiSuggestions.AsNoTracking()
                    .Where(s => s.Status == "PENDING_REVIEW")
                    .OrderByDescending(s => s.CreatedAt)
                    .Take(4)
                    .ToListAsync(cancellationToken);

                if (recentAi.Count > 0)
                {
                    sb.AppendLine("\n* CẢNH BÁO PHÂN TÍCH AI ĐANG CHỜ DUYỆT:");
                    foreach (var a in recentAi)
                    {
                        string reason = "";
                        if (!string.IsNullOrWhiteSpace(a.PayloadJson))
                        {
                            try
                            {
                                using var doc = JsonDocument.Parse(a.PayloadJson);
                                if (doc.RootElement.TryGetProperty("reason", out var rProp))
                                    reason = rProp.GetString() ?? "";
                            }
                            catch { }
                        }
                        sb.AppendLine($"  + Loại: {a.SuggestionType} | SKU: {a.SkuId ?? "N/A"} | Lý do: {reason}");
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"[Lưu ý: Không thể trích xuất đầy đủ CSDL: {ex.Message}]");
            }

            return sb.ToString();
        }
    }
}
