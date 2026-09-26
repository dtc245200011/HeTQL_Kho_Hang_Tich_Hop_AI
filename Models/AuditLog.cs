using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DuAnCode.Web.Models
{
    public class AuditLog
    {
        [Key]
        public string LogId { get; set; } = Guid.NewGuid().ToString();
        // Compatibility property: some parts of the codebase expect an "Id" member.
        // Keep LogId as the EF key but provide Id as an alias to avoid CS1061 errors.
        public string Id { get => LogId; set => LogId = value; }
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? BeforeJson { get; set; }
        public string? AfterJson { get; set; }
        public string PerformedBy { get; set; } = string.Empty;
        public DateTime PerformedAt { get; set; } = DateTime.UtcNow;
        public string? Reason { get; set; }
    }
}
