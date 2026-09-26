using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class StockTransferLine
    {
        [Key]
        public int Id { get; set; }
        public string TransferId { get; set; } = string.Empty;
        public string SkuId { get; set; } = string.Empty;
        public int Qty { get; set; }

        public StockTransfer? Transfer { get; set; }
    }
}
