using System.Collections.Generic;
// no controller references here

namespace DuAnCode.Web.Models.ViewModels
{
    public class ReportViewModel
    {
        public IEnumerable<object> Warehouses { get; set; } = new List<object>();
        public IEnumerable<ReportRow> ReportRows { get; set; } = new List<ReportRow>();
    }

    public class ReportRow
    {
        public string SkuId { get; set; } = null!;
        public int Beginning { get; set; }
        public int TotalIn { get; set; }
        public int TotalOut { get; set; }
        public int Ending { get; set; }
    }
}
