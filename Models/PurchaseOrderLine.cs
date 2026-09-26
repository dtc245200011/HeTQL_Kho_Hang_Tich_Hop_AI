using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DuAnCode.Web.Models
{
    public class PurchaseOrderLine
    {
        [Key]
        public int Id { get; set; }
        public string PoId { get; set; } = string.Empty;
        public string SkuId { get; set; } = string.Empty;
        public int QtyDocument { get; set; }
        public int QtyActualGood { get; set; }
        public int QtyActualDamaged { get; set; }
        public decimal UnitCost { get; set; }
        public string? VarianceReason { get; set; }

        public PurchaseOrder? PurchaseOrder { get; set; }
    }
}
