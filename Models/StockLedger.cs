namespace DuAnCode.Web.Models
{
    public class StockLedger
    {
        public string SkuId { get; set; } = null!;
        public string WarehouseId { get; set; } = null!;
        public string Status { get; set; } = null!; // GOOD, DAMAGED, RESERVED
        public int Quantity { get; set; }
    }
}
