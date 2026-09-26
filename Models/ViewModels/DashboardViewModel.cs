using System.Collections.Generic;

namespace DuAnCode.Web.Models.ViewModels
{
    public class DashboardViewModel
    {
        public int TotalSkus { get; set; }
        public int TotalInventory { get; set; }
        public int Damaged { get; set; }
        public int LowStockCount { get; set; }
        public IEnumerable<StockLedgerVm> Ledgers { get; set; } = new List<StockLedgerVm>();
    }
}
