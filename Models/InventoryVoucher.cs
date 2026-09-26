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
        public string WarehouseId { get; set; } = string.Empty;
        public decimal TotalAmount { get; set; }

        public List<VoucherLine> Lines { get; set; } = new();
    }
}
