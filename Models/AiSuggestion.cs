using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class AiSuggestion
    {
        [Key]
        public string SuggestionId { get; set; } = Guid.NewGuid().ToString();
        public string SuggestionType { get; set; } = string.Empty; // REPLENISHMENT, ANOMALY, BOTTLENECK, REPORT_NARRATIVE
        public string? SkuId { get; set; }
        public string PayloadJson { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING_REVIEW"; // PENDING_REVIEW, APPROVED, REJECTED
        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
