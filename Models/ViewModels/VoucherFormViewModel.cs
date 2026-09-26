namespace DuAnCode.Web.Models.ViewModels
{
    public class VoucherLineInput
    {
        public int? Id { get; set; }
        public string SkuId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = "Cái";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
    }

    public class VoucherFormViewModel
    {
        // Optional VoucherId when editing existing vouchers
        public string? VoucherId { get; set; }
        public string? VoucherNumber { get; set; }
        public DateTime Date { get; set; } = DateTime.UtcNow;
        public string Counterparty { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string WarehouseId { get; set; } = string.Empty;
        public List<VoucherLineInput> Lines { get; set; } = new();
    }
}