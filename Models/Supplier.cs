namespace DuAnCode.Web.Models
{
    public class Supplier
    {
        public string SupplierId { get; set; } = null!;
        public string TaxCode { get; set; } = null!;
        public string Name { get; set; } = null!;
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
