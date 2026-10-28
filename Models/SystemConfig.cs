namespace DuAnCode.Web.Models
{
    public class SystemConfig
    {
        public int Id { get; set; }
        // Company / Brand name
        public string CompanyName { get; set; } = "Kho Tổng WH-GLOBAL";
        public string Address { get; set; } = "123 Đường ABC, Hà Nội";
        public string Phone { get; set; } = "0901234567";
        public string? Email { get; set; } = "admin@duancode.com";
        public bool AutoBackup { get; set; } = false;

        // Stop Configurations
        public bool MaintenanceMode { get; set; } = false;
        public bool StopInbound { get; set; } = false;
        public bool StopOutbound { get; set; } = false;
        public bool StopApi { get; set; } = false;

        // Alert Configurations
        public int LowStockAlertThreshold { get; set; } = 10;
        public int CapacityAlertPercent { get; set; } = 90;
        public bool EnableEmailAlerts { get; set; } = true;

        // 1. Voucher Prefix
        public string InboundPrefix { get; set; } = "PN-";
        public string OutboundPrefix { get; set; } = "PX-";

        // 2. Barcode / QR Label
        public string BarcodeFormat { get; set; } = "QR"; // "QR" or "BARCODE"
        public int LabelWidth { get; set; } = 50; // mm
        public int LabelHeight { get; set; } = 50; // mm
        public bool PrintPriceOnLabel { get; set; } = true;
        public bool PrintExpiryOnLabel { get; set; } = true;

        // 3. Ollama (AI) Connection
        public string OllamaUrl { get; set; } = "http://localhost:11434";
        public string OllamaModel { get; set; } = "qwen2.5-coder:7b";
        public int AiScanIntervalMinutes { get; set; } = 30;

        // 4. I18N and Localization
        public string DefaultLanguage { get; set; } = "vi-VN"; // "vi-VN" or "en-US"
        public string CurrencyFormat { get; set; } = "VND"; // "VND" or "USD"

        // 5. Cấu hình Quy trình Phê duyệt (Approval Workflow)
        public bool EnableMultiLevelApproval { get; set; } = true; // Bật quy trình duyệt phân cấp (Cấp 1 -> Cấp 2 -> Cấp 3)
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal OutboundDirectorApprovalThreshold { get; set; } = 100000000M; // Ngưỡng giá trị phiếu xuất cần Giám đốc phê duyệt (VNĐ)
        public bool InboundRequireApproval { get; set; } = true; // Yêu cầu duyệt 2 cấp đối với Phiếu Nhập
        public bool TransferRequireApproval { get; set; } = true; // Yêu cầu duyệt đối với Phiếu Chuyển kho nội bộ
        public string ApprovalRoleLevel1 { get; set; } = "Accountant"; // Vai trò duyệt Cấp 1 (Mặc định: Kế toán)
        public string ApprovalRoleLevel2 { get; set; } = "WarehouseManager"; // Vai trò duyệt Cấp 2 (Mặc định: Quản lý kho)
        public string ApprovalRoleLevel3 { get; set; } = "Director"; // Vai trò duyệt Cấp 3 (Mặc định: Giám đốc)
        public bool AutoApproveLowValueOutbound { get; set; } = false; // Tự động duyệt phiếu xuất giá trị nhỏ
        [System.ComponentModel.DataAnnotations.Schema.Column(TypeName = "decimal(18,2)")]
        public decimal AutoApproveThreshold { get; set; } = 0M; // Ngưỡng giá trị tự động duyệt (VNĐ)
    }
}
