using System.Text.Json;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class SettingsController : Controller
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        public SettingsController(IWebHostEnvironment env, IConfiguration config)
        {
            _env = env;
            _config = config;
        }

        [HttpGet]
        public IActionResult Database()
        {
            var cs = _config.GetConnectionString("DefaultConnection");
            var vm = new DatabaseSettingsViewModel();

            if (!string.IsNullOrEmpty(cs))
            {
                try
                {
                    var builder = new SqlConnectionStringBuilder(cs);
                    vm.ServerName = builder.DataSource ?? vm.ServerName;
                    vm.DatabaseName = builder.InitialCatalog ?? vm.DatabaseName;
                    vm.UseWindowsAuthentication = builder.IntegratedSecurity;
                    vm.EnableMARS = builder.MultipleActiveResultSets;
                    vm.TrustServerCertificate = builder.TrustServerCertificate;
                    if (!builder.IntegratedSecurity)
                    {
                        vm.Username = builder.UserID ?? string.Empty;
                    }
                }
                catch { }
            }

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Database(DatabaseSettingsViewModel vm, string action)
        {
            if (action == "test")
            {
                // Should be handled via AJAX TestConnection
                return RedirectToAction("Database");
            }

            // Build connection string and save to appsettings.json
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = vm.ServerName,
                InitialCatalog = vm.DatabaseName,
                MultipleActiveResultSets = vm.EnableMARS,
                TrustServerCertificate = vm.TrustServerCertificate
            };

            if (vm.UseWindowsAuthentication)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = vm.Username;
                builder.Password = vm.Password;
            }

            // Update appsettings.json
            var file = Path.Combine(_env.ContentRootPath, "appsettings.json");
            var text = System.IO.File.ReadAllText(file);
            var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var options = new JsonSerializerOptions { WriteIndented = true };

            var connObj = new Dictionary<string, object>
            {
                ["DefaultConnection"] = builder.ConnectionString
            };

            // Reconstruct new appsettings content merging existing other sections
            var newObj = new Dictionary<string, object?>();
            foreach (var prop in root.EnumerateObject())
            {
                if (prop.NameEquals("ConnectionStrings")) continue;
                newObj[prop.Name] = JsonSerializer.Deserialize<object>(prop.Value.GetRawText());
            }
            newObj["ConnectionStrings"] = connObj;

            var newText = JsonSerializer.Serialize(newObj, options);
            System.IO.File.WriteAllText(file, newText);

            TempData["Message"] = "Đã lưu cấu hình kết nối. Vui lòng khởi động lại ứng dụng nếu cần.";
            return RedirectToAction("Database");
        }

        [HttpPost]
        public async Task<IActionResult> TestConnection([FromBody] DatabaseSettingsViewModel vm)
        {
            if (vm == null) return BadRequest(new { ok = false, message = "Dữ liệu không hợp lệ." });

            var builder = new SqlConnectionStringBuilder
            {
                DataSource = vm.ServerName,
                InitialCatalog = vm.DatabaseName,
                MultipleActiveResultSets = vm.EnableMARS,
                TrustServerCertificate = vm.TrustServerCertificate
            };

            if (vm.UseWindowsAuthentication)
            {
                builder.IntegratedSecurity = true;
            }
            else
            {
                builder.IntegratedSecurity = false;
                builder.UserID = vm.Username;
                builder.Password = vm.Password;
            }

            try
            {
                using var conn = new SqlConnection(builder.ConnectionString);
                await conn.OpenAsync();
                // quick check for MARS if requested
                if (vm.EnableMARS && !conn.ConnectionString.Contains("MultipleActiveResultSets=true", StringComparison.OrdinalIgnoreCase))
                {
                    // not strictly reliable; let it pass
                }

                await conn.CloseAsync();
                return Json(new { ok = true, message = "Kết nối CSDL thành công." });
            }
            catch (SqlException ex)
            {
                string msg = ex.Message;
                if (ex.Number == 53 || ex.Number == -1) // Server not found or network related
                {
                    msg = "Không thể kết nối đến máy chủ SQL. Kiểm tra ServerName, cổng và firewall. (Lỗi 40/53)";
                }
                else if (ex.Number == 18456)
                {
                    msg = "Xác thực thất bại: kiểm tra tên đăng nhập / mật khẩu (Lỗi 18456).";
                }
                else if (ex.Message.Contains("MARS", StringComparison.OrdinalIgnoreCase))
                {
                    msg = "Lỗi liên quan đến Multiple Active Result Sets (MARS). Hãy bật tùy chọn EnableMARS.";
                }

                return Json(new { ok = false, message = msg, detail = ex.Message });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, message = "Lỗi: " + ex.Message, detail = ex.ToString() });
            }
        }
    }
}
