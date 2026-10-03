using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class InventoryVoucher
    {
        [Key]
        public string VoucherId { get; set; } = Guid.NewGuid().ToString();
        public string VoucherNumber { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Type { get; set; } = "INBOUND"; // INBOUND / OUTBOUND
        public string Counterparty { get; set; } = string.Empty; // Supplier or Receiver
        public string Address { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string ReferenceDocument { get; set; } = string.Empty; // Chứng từ NCC (REQ-F-03)
        public string WarehouseId { get; set; } = string.Empty;
        public string? ToWarehouseId { get; set; } // Used for TRANSFER type
        public decimal TotalAmount { get; set; }
        
        public string Status { get; set; } = "DRAFT"; // DRAFT, PENDING, APPROVED, REJECTED
        public bool IsLocked { get; set; } = false;
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }

        public List<VoucherLine> Lines { get; set; } = new();
    }
}
