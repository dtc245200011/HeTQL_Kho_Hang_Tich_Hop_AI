using DuAnCode.Web.Models.ViewModels;

namespace DuAnCode.Web.Models.ViewModels
{
    public class HomeIndexViewModel
    {
        public string Tab { get; set; } = "all";
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public bool ViewAll { get; set; } = false;
        public int PageSize { get; set; } = 5;

        public List<StockLedgerVm> Inventory { get; set; } = new();
        public int InventoryTotal { get; set; }

        public List<Models.StockMovement> Inbound { get; set; } = new();
        public int InboundTotal { get; set; }

        public List<Models.StockMovement> Outbound { get; set; } = new();
        public int OutboundTotal { get; set; }

        // Optional full lists for modals
        public List<StockLedgerVm>? AllInventory { get; set; }
        public List<Models.StockMovement>? AllInbound { get; set; }
        public List<Models.StockMovement>? AllOutbound { get; set; }
    }
}