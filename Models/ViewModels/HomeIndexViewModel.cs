using DuAnCode.Web.Models.ViewModels;

namespace DuAnCode.Web.Models.ViewModels
{
    public class PendingActionItemVm
    {
        public string Title { get; set; } = string.Empty;
        public string Code { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // Inbound, Outbound, Audit, LowStock
        public string BadgeClass { get; set; } = "bg-warning";
        public string Url { get; set; } = "#";
        public DateTime Time { get; set; }
        public string Description { get; set; } = string.Empty;
        public string IconClass { get; set; } = "fa-solid fa-circle-exclamation";
    }

    public class HomeIndexViewModel
    {
        public string Tab { get; set; } = "all";
        public string Search { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Page { get; set; } = 1;
        public bool ViewAll { get; set; } = false;
        public int PageSize { get; set; } = 5;

        // KPI Statistics for Overview
        public int TotalSkus { get; set; }
        public int TotalAvailableQuantity { get; set; }
        public int TotalDamagedQuantity { get; set; }
        public int LowStockCount { get; set; }
        public int PendingVouchersCount { get; set; }

        // Action items / Alerts for new users
        public List<PendingActionItemVm> ActionItems { get; set; } = new();

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