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
    }
}
