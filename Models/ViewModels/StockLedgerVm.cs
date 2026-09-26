namespace DuAnCode.Web.Models.ViewModels
{
    public class StockLedgerVm
    {
        public string SkuId { get; set; } = null!;
        public string ProductName { get; set; } = null!;
        public string WarehouseId { get; set; } = null!;
        public string Status { get; set; } = null!;
        public int Quantity { get; set; }
    }
}
