using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace DuAnCode.Web.Models
{
    public class VoucherLine
    {
        [Key]
        public int Id { get; set; }
        public string VoucherId { get; set; } = string.Empty;
        public string SkuId { get; set; } = string.Empty;
        public string SkuCode { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public string Unit { get; set; } = "Cái";
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal LineTotal { get; set; }

        [ForeignKey("VoucherId")]
        public InventoryVoucher? Voucher { get; set; }
    }
}
