using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class BomComponent
    {
        [Key]
        public int Id { get; set; }
        public string ComboId { get; set; } = string.Empty;
        public string SkuId { get; set; } = string.Empty;
        public int QuantityPerSet { get; set; }

        public ComboProduct? Combo { get; set; }
    }
}
