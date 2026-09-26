using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class StockAdjustment
    {
        [Key]
        public string AdjustmentId { get; set; } = Guid.NewGuid().ToString();
        public string SkuId { get; set; } = string.Empty;
        public string WarehouseId { get; set; } = string.Empty;
        public int OldQuantity { get; set; }
        public int NewQuantity { get; set; }
        public string? Reason { get; set; }
        public DateTime AdjustedAt { get; set; } = DateTime.UtcNow;
        public string? PerformedBy { get; set; }
    }
}
