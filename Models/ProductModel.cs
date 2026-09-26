namespace DuAnCode.Web.Models
{
    public class ProductModel
    {
        public string ProductModelId { get; set; } = null!;
        public string ProductName { get; set; } = null!;
        public string? Category { get; set; }
        public string Unit { get; set; } = "pcs";
        public int MinStock { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
