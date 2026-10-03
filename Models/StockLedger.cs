namespace DuAnCode.Web.Models
{
    public class StockLedger
    {
        public string SkuId { get; set; } = null!;
        public string WarehouseId { get; set; } = null!;
        public string Status { get; set; } = null!; // GOOD, DAMAGED, RESERVED
        public string BatchNumber { get; set; } = string.Empty; // Mã lô
        public DateTime? ExpiryDate { get; set; }
        public int Quantity { get; set; }
    }
}
