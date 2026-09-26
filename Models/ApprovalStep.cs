using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class ApprovalStep
    {
        [Key]
        public string StepId { get; set; } = Guid.NewGuid().ToString();
        public string EntityType { get; set; } = string.Empty; // PO/SO
        public string EntityId { get; set; } = string.Empty;
        public int Level { get; set; }
        public string ApproverRole { get; set; } = string.Empty;
        public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
    }
}
