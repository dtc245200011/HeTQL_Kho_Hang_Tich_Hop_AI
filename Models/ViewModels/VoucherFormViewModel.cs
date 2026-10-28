using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models.ViewModels
{
    public class VoucherLineInput
    {
        public int? Id { get; set; }
        
        [Required(ErrorMessage = "Vui lòng chọn Sản phẩm/SKU")]
        public string SkuId { get; set; } = string.Empty;
        
        [Required(ErrorMessage = "Vui lòng nhập Tên sản phẩm")]
        public string ProductName { get; set; } = string.Empty;
        
        public string Unit { get; set; } = "Cái";

        [Range(1, int.MaxValue, ErrorMessage = "Số lượng thực nhập/xuất phải lớn hơn 0")]
        public int Quantity { get; set; } = 1;

        [Range(0, int.MaxValue, ErrorMessage = "Số lượng chứng từ không được âm")]
        public int DocumentQuantity { get; set; } = 1;
        
        public string BatchNumber { get; set; } = string.Empty;
        
        public DateTime? ExpiryDate { get; set; }

        [Range(0, double.MaxValue, ErrorMessage = "Đơn giá không được âm")]
        public decimal UnitPrice { get; set; } = 0;
    }

    public class VoucherFormViewModel
    {
        // Optional VoucherId when editing existing vouchers
        public string? VoucherId { get; set; }
        public string? VoucherNumber { get; set; }
        public DateTime? Date { get; set; } = DateTime.UtcNow;

        [Required(ErrorMessage = "Vui lòng nhập Tên đối tác/Khách hàng")]
        public string Counterparty { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Địa chỉ")]
        public string Address { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng nhập Lý do")]
        public string Reason { get; set; } = string.Empty;

        public string ReferenceDocument { get; set; } = string.Empty;

        [Required(ErrorMessage = "Vui lòng chọn Kho hàng")]
        public string WarehouseId { get; set; } = string.Empty;
        public string? ToWarehouseId { get; set; }
        public List<VoucherLineInput> Lines { get; set; } = new();

        // Status & Rejection details
        public string Status { get; set; } = "DRAFT";
        public bool IsLocked { get; set; } = false;
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectionReason { get; set; }
        public string? OriginalReason { get; set; }
    }
}