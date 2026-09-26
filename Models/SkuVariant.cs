namespace DuAnCode.Web.Models
{
    public class SkuVariant
    {
        public string SkuId { get; set; } = null!;
        public string ProductModelId { get; set; } = null!;
        public string? Color { get; set; }
        public string? Material { get; set; }
        public int LengthMm { get; set; }
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public decimal Cbm { get; set; }
        public decimal UnitPrice { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
