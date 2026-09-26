using DuAnCode.Web.Models;

namespace DuAnCode.Web.Models.ViewModels
{
    public class VoucherPageViewModel
    {
        public List<SkuDto> Skus { get; set; } = new();
        public List<Warehouse> Warehouses { get; set; } = new();
        public List<InventoryVoucher> Recent { get; set; } = new();
        public VoucherFormViewModel Form { get; set; } = new();
    }
}