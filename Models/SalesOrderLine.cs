using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class SalesOrderLine
    {
        [Key]
        public int LineId { get; set; }
        public string SoId { get; set; } = string.Empty;
        public string? SkuId { get; set; }
        public string? ComboId { get; set; }
        public int Qty { get; set; }
        public decimal UnitPrice { get; set; }

        public SalesOrder? SalesOrder { get; set; }
    }
}
