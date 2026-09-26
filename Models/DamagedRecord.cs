using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class DamagedRecord
    {
        [Key]
        public string DamagedId { get; set; } = Guid.NewGuid().ToString();
        public string SkuId { get; set; } = string.Empty;
        public string WarehouseId { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string Action { get; set; } = string.Empty; // LIQUIDATE, RETURN_SUPPLIER, REPAIR
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public string? CreatedBy { get; set; }
    }
}
