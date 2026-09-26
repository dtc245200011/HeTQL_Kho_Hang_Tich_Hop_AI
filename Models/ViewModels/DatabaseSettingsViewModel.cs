using System.ComponentModel.DataAnnotations;

namespace DuAnCode.Web.Models.ViewModels
{
    public class DatabaseSettingsViewModel
    {
        [Display(Name = "Server")]
        public string ServerName { get; set; } = ".\\SQLEXPRESS";

        [Display(Name = "Tên CSDL")]
        public string DatabaseName { get; set; } = "FurnitureWms";

        [Display(Name = "Sử dụng xác thực Windows")]
        public bool UseWindowsAuthentication { get; set; } = true;

        [Display(Name = "Tên đăng nhập (SQL)")]
        public string Username { get; set; } = string.Empty;

        [Display(Name = "Mật khẩu (SQL)")]
        public string Password { get; set; } = string.Empty;

        [Display(Name = "Enable MARS")]
        public bool EnableMARS { get; set; } = true;

        [Display(Name = "Trust Server Certificate")]
        public bool TrustServerCertificate { get; set; } = true;
    }
}
