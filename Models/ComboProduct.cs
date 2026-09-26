using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models
{
    public class ComboProduct
    {
        [Key]
        public string ComboId { get; set; } = Guid.NewGuid().ToString();
        public string ComboName { get; set; } = string.Empty;
        public decimal? CbmOverride { get; set; }
        public int MinStock { get; set; }

        public List<BomComponent> Components { get; set; } = new();
    }
}
