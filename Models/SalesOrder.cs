using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class SalesOrder
    {
        [Key]
        public string SoId { get; set; } = Guid.NewGuid().ToString();
        public DateTime SoDate { get; set; } = DateTime.UtcNow;
        public string WarehouseId { get; set; } = string.Empty;
        public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_L1, PENDING_L2, PENDING_L3, APPROVED, CANCELLED
        public decimal TotalAmount { get; set; }
        public string? CreatedBy { get; set; }

        public List<SalesOrderLine> Lines { get; set; } = new();
    }
}
