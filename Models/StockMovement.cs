using System;

namespace DuAnCode.Web.Models
{
    public class StockMovement
    {
        public long MovementId { get; set; }
        public string SkuId { get; set; } = null!;
        public string WarehouseId { get; set; } = null!;
        public string StockStatus { get; set; } = null!; // GOOD / DAMAGED
        public int QuantityDelta { get; set; }
        public string MovementType { get; set; } = null!; // INBOUND / OUTBOUND / TRANSFER / ADJUSTMENT / INITIAL
        public string? ReferenceType { get; set; }
        public string? ReferenceId { get; set; }
        public string? PerformedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
