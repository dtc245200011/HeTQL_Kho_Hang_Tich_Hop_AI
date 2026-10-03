using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class InventoryAudit
    {
        [Key]
        public string AuditId { get; set; } = Guid.NewGuid().ToString();
        public string AuditNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string WarehouseId { get; set; } = string.Empty;
        public string Status { get; set; } = "DRAFT"; // DRAFT, APPROVED
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public string? Reason { get; set; }
        public List<InventoryAuditLine> Lines { get; set; } = new();
    }

    public class InventoryAuditLine
    {
        [Key]
        public int Id { get; set; }
        public string AuditId { get; set; } = string.Empty;
        
        [System.ComponentModel.DataAnnotations.Schema.ForeignKey("AuditId")]
        public InventoryAudit? Audit { get; set; }
        public string SkuId { get; set; } = string.Empty;
        public string BatchNumber { get; set; } = string.Empty;
        public int SystemQuantity { get; set; }
        public int ActualQuantity { get; set; }
        public string? Notes { get; set; }
    }
}
