using System.Collections.Generic;

namespace DuAnCode.Web.Models
{
    public class SettingsViewModel
    {
        public SystemConfig Config { get; set; } = new SystemConfig();
        public List<Warehouse> Warehouses { get; set; } = new List<Warehouse>();
    }
}
