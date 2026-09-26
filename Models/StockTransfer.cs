using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class StockTransfer
    {
        [Key]
        public string TransferId { get; set; } = Guid.NewGuid().ToString();
        public string FromWarehouseId { get; set; } = string.Empty;
        public string ToWarehouseId { get; set; } = string.Empty;
        public DateTime TransferDate { get; set; } = DateTime.UtcNow;
        public string? PerformedBy { get; set; }

        public List<StockTransferLine> Lines { get; set; } = new();
    }
}
