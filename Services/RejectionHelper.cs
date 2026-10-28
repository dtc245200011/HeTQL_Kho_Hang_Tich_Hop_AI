using System;

namespace DuAnCode.Web.Services
{
    public class RejectionDetailDto
    {
        public string EntityId { get; set; } = string.Empty;
        public string EntityNumber { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty; // Inbound, Outbound, Transfer, Audit
        public string? CreatedBy { get; set; }
        public DateTime? CreatedAt { get; set; }
        public string? RejectedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string RejectionReason { get; set; } = string.Empty;
        public string? OriginalReason { get; set; }
        public string? ApproverRole { get; set; }
    }

    public static class RejectionHelper
    {
        /// <summary>
        /// Bóc tách lý do từ chuỗi ghi chú của phiếu.
        /// Chuỗi lưu khi từ chối thường có dạng: "[TỪ CHỐI DUYỆT: {lý do}]" hoặc "{Lý do gốc} | [TỪ CHỐI DUYỆT: {lý do}]"
        /// </summary>
        public static (string OriginalReason, string RejectionReason) ExtractReason(string? fullReason)
        {
            if (string.IsNullOrWhiteSpace(fullReason))
                return (string.Empty, "Không có lý do cụ thể");

            const string marker = "[TỪ CHỐI DUYỆT:";
            int idx = fullReason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var original = fullReason.Substring(0, idx).Trim().TrimEnd('|', '-').Trim();
                var afterMarker = fullReason.Substring(idx + marker.Length);
                int endIdx = afterMarker.IndexOf(']');
                var reject = endIdx >= 0 ? afterMarker.Substring(0, endIdx).Trim() : afterMarker.Trim();
                if (string.IsNullOrWhiteSpace(reject)) reject = "Không có lý do cụ thể";
                return (original, reject);
            }

            return (string.Empty, fullReason.Trim());
        }
    }
}
