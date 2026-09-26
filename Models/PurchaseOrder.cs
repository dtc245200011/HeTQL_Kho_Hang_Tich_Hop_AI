using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DuAnCode.Web.Models
{
    public class PurchaseOrder
    {
        [Key]
        public string PoId { get; set; } = Guid.NewGuid().ToString();
        public DateTime PoDate { get; set; } = DateTime.UtcNow;
        public string SupplierId { get; set; } = string.Empty;
        public string WarehouseId { get; set; } = string.Empty;
        public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING_L1, APPROVED, CANCELLED
        public decimal TotalAmount { get; set; }
        public string? CreatedBy { get; set; }

        public List<PurchaseOrderLine> Lines { get; set; } = new();
    }
}
