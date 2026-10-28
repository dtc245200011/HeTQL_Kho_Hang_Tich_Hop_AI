using DuAnCode.Web.Data;
using DuAnCode.Web.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _db;
        public HomeController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index(string tab = "all", string search = "", string status = "", int page = 1, bool viewAll = false)
        {
            var vm = new HomeIndexViewModel()
            {
                Tab = tab,
                Search = search ?? string.Empty,
                Status = status ?? string.Empty,
                Page = page <= 0 ? 1 : page,
                ViewAll = viewAll
            };

            vm.PageSize = viewAll ? 50 : 5;

            // Inventory (StockLedgers)
            var invQuery = _db.StockLedgers.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(vm.Search))
            {
                var s = vm.Search.Trim();
                var matchingSkuIds = await _db.SkuVariants.AsNoTracking()
                    .Where(sv => sv.SkuId.Contains(s) || (sv.Color != null && sv.Color.Contains(s)) || (sv.Material != null && sv.Material.Contains(s)))
                    .Select(sv => sv.SkuId)
                    .ToListAsync();
                var matchingPmIds = await _db.ProductModels.AsNoTracking()
                    .Where(pm => pm.ProductName.Contains(s))
                    .Select(pm => pm.ProductModelId)
                    .ToListAsync();
                var skusFromPm = await _db.SkuVariants.AsNoTracking()
                    .Where(sv => matchingPmIds.Contains(sv.ProductModelId))
                    .Select(sv => sv.SkuId)
                    .ToListAsync();

                var allMatched = matchingSkuIds.Union(skusFromPm).ToHashSet();
                invQuery = invQuery.Where(x => x.SkuId.Contains(s) || allMatched.Contains(x.SkuId));
            }
            if (!string.IsNullOrWhiteSpace(vm.Status))
            {
                invQuery = invQuery.Where(x => x.Status == vm.Status);
            }

            vm.InventoryTotal = await invQuery.CountAsync();
            var invOrdered = invQuery.OrderByDescending(x => x.SkuId);
            var invPaged = await invOrdered.Skip((vm.Page - 1) * vm.PageSize).Take(vm.PageSize).ToListAsync();

            // Map to VM with product name lookup
            var skuIds = invPaged.Select(x => x.SkuId).Distinct().ToList();
            var skuMap = await _db.SkuVariants.AsNoTracking().Where(s => skuIds.Contains(s.SkuId)).ToListAsync();
            var pmIds = skuMap.Select(s => s.ProductModelId).Distinct().ToList();
            var pmMap = await _db.ProductModels.AsNoTracking().Where(p => pmIds.Contains(p.ProductModelId)).ToListAsync();

            vm.Inventory = invPaged.Select(s => new StockLedgerVm
            {
                SkuId = s.SkuId,
                WarehouseId = s.WarehouseId,
                Status = s.Status,
                Quantity = s.Quantity,
                ProductName = pmMap.FirstOrDefault(p => p.ProductModelId == skuMap.FirstOrDefault(x => x.SkuId == s.SkuId)?.ProductModelId)?.ProductName ?? string.Empty
            }).ToList();
            // Optional: provide full inventory list for modals when requested; otherwise avoid heavy queries
            if (vm.ViewAll)
            {
                var invList = await invOrdered.ToListAsync();
                var allSkuIds = invList.Select(x => x.SkuId).Distinct().ToList();
                var allSkuMap = await _db.SkuVariants.AsNoTracking().Where(s => allSkuIds.Contains(s.SkuId)).ToListAsync();
                var allPmIds = allSkuMap.Select(s => s.ProductModelId).Distinct().ToList();
                var allPmMap = await _db.ProductModels.AsNoTracking().Where(p => allPmIds.Contains(p.ProductModelId)).ToListAsync();

                vm.AllInventory = invList.Select(s => new StockLedgerVm
                {
                    SkuId = s.SkuId,
                    WarehouseId = s.WarehouseId,
                    Status = s.Status,
                    Quantity = s.Quantity,
                    ProductName = allPmMap.FirstOrDefault(p => p.ProductModelId == allSkuMap.FirstOrDefault(x => x.SkuId == s.SkuId)?.ProductModelId)?.ProductName ?? string.Empty
                }).ToList();
            }
            else
            {
                vm.AllInventory = null;
            }

            // Inbound and Outbound (StockMovements)
            var inboundQuery = _db.StockMovements.AsNoTracking().Where(m => m.MovementType == "INBOUND");
            var outboundQuery = _db.StockMovements.AsNoTracking().Where(m => m.MovementType == "OUTBOUND");

            if (!string.IsNullOrWhiteSpace(vm.Search))
            {
                var s = vm.Search.Trim();
                inboundQuery = inboundQuery.Where(x => x.SkuId.Contains(s));
                outboundQuery = outboundQuery.Where(x => x.SkuId.Contains(s));
            }

            vm.InboundTotal = await inboundQuery.CountAsync();
            vm.OutboundTotal = await outboundQuery.CountAsync();
            // Provide full lists for modals (server-side rendering or AJAX). These are intentionally loaded here so modals are always populated
            vm.AllInbound = await inboundQuery.OrderByDescending(x => x.CreatedAt).ToListAsync();
            vm.AllOutbound = await outboundQuery.OrderByDescending(x => x.CreatedAt).ToListAsync();

            var inOrdered = inboundQuery.OrderByDescending(x => x.CreatedAt);
            var outOrdered = outboundQuery.OrderByDescending(x => x.CreatedAt);

            vm.Inbound = await inOrdered.Skip((vm.Page - 1) * vm.PageSize).Take(vm.PageSize).ToListAsync();
            vm.Outbound = await outOrdered.Skip((vm.Page - 1) * vm.PageSize).Take(vm.PageSize).ToListAsync();

            // 1. KPI Statistics
            vm.TotalSkus = await _db.SkuVariants.AsNoTracking().CountAsync();
            vm.TotalAvailableQuantity = await _db.StockLedgers.AsNoTracking()
                .Where(x => x.Status == "GOOD" || x.Status == "Available")
                .SumAsync(x => (int?)x.Quantity) ?? 0;
            vm.TotalDamagedQuantity = await _db.StockLedgers.AsNoTracking()
                .Where(x => x.Status == "DAMAGED" || x.Status == "Damaged")
                .SumAsync(x => (int?)x.Quantity) ?? 0;
            vm.LowStockCount = await _db.StockLedgers.AsNoTracking()
                .Where(x => (x.Status == "GOOD" || x.Status == "Available") && x.Quantity <= 5)
                .CountAsync();

            var pendingVouchers = await _db.InventoryVouchers.AsNoTracking()
                .Where(v => v.Status == "PENDING")
                .OrderByDescending(v => v.Date)
                .Take(5)
                .ToListAsync();
            vm.PendingVouchersCount = await _db.InventoryVouchers.AsNoTracking()
                .Where(v => v.Status == "PENDING")
                .CountAsync();

            // 2. Action items / Alerts for new users
            vm.ActionItems = new List<PendingActionItemVm>();

            // Pending Inbound & Outbound vouchers
            foreach (var pv in pendingVouchers)
            {
                var isIb = pv.Type == "INBOUND";
                vm.ActionItems.Add(new PendingActionItemVm
                {
                    Title = isIb ? "Phiếu Nhập kho chờ duyệt" : "Phiếu Xuất kho chờ duyệt",
                    Code = string.IsNullOrWhiteSpace(pv.VoucherNumber) ? pv.VoucherId[..Math.Min(8, pv.VoucherId.Length)] : pv.VoucherNumber,
                    Type = isIb ? "Nhập kho" : "Xuất kho",
                    BadgeClass = isIb ? "bg-primary" : "bg-warning text-dark",
                    Url = isIb ? $"/Inbound/Details/{pv.VoucherId}" : $"/Outbound/Details/{pv.VoucherId}",
                    Time = pv.Date,
                    Description = $"Đối tác: {pv.Counterparty} • Trị giá: {pv.TotalAmount:N0} đ",
                    IconClass = isIb ? "fa-solid fa-box-arrow-in-down text-primary" : "fa-solid fa-box-arrow-up text-warning"
                });
            }

            // Low stock warning items (top 3)
            var lowStockItems = await (from sl in _db.StockLedgers.AsNoTracking()
                                      where (sl.Status == "GOOD" || sl.Status == "Available") && sl.Quantity <= 5
                                      join sv in _db.SkuVariants.AsNoTracking() on sl.SkuId equals sv.SkuId into svj
                                      from sv in svj.DefaultIfEmpty()
                                      join pm in _db.ProductModels.AsNoTracking() on sv.ProductModelId equals pm.ProductModelId into pmj
                                      from pm in pmj.DefaultIfEmpty()
                                      orderby sl.Quantity ascending
                                      select new {
                                          sl.SkuId,
                                          sl.Quantity,
                                          sl.WarehouseId,
                                          ProductName = pm != null ? pm.ProductName : sl.SkuId
                                      }).Take(3).ToListAsync();

            foreach (var ls in lowStockItems)
            {
                vm.ActionItems.Add(new PendingActionItemVm
                {
                    Title = $"Cảnh báo tồn kho thấp ({ls.Quantity} cái)",
                    Code = ls.SkuId,
                    Type = "Cảnh báo tồn",
                    BadgeClass = "bg-danger",
                    Url = $"/Home/Index?tab=inventory&search={ls.SkuId}",
                    Time = DateTime.UtcNow,
                    Description = $"{ls.ProductName} ({ls.WarehouseId}) sắp hết hàng",
                    IconClass = "fa-solid fa-triangle-exclamation text-danger"
                });
            }

            // Draft audits needing completion
            var draftAudits = await _db.InventoryAudits.AsNoTracking()
                .Where(a => a.Status == "DRAFT")
                .OrderByDescending(a => a.Date)
                .Take(2)
                .ToListAsync();

            foreach (var da in draftAudits)
            {
                vm.ActionItems.Add(new PendingActionItemVm
                {
                    Title = "Phiếu Kiểm kê chưa hoàn tất",
                    Code = string.IsNullOrWhiteSpace(da.AuditNumber) ? da.AuditId[..Math.Min(8, da.AuditId.Length)] : da.AuditNumber,
                    Type = "Kiểm kê",
                    BadgeClass = "bg-info text-white",
                    Url = $"/InventoryAudit/Details/{da.AuditId}",
                    Time = da.Date,
                    Description = $"Kho {da.WarehouseId} - Đang kiểm tra số lượng thực tế",
                    IconClass = "fa-solid fa-clipboard-check text-info"
                });
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllInventory()
        {
            var list = await (from sl in _db.StockLedgers.AsNoTracking()
                              join sv in _db.SkuVariants.AsNoTracking() on sl.SkuId equals sv.SkuId into svj
                              from sv in svj.DefaultIfEmpty()
                              join pm in _db.ProductModels.AsNoTracking() on sv.ProductModelId equals pm.ProductModelId into pmj
                              from pm in pmj.DefaultIfEmpty()
                              orderby sl.SkuId descending
                              select new {
                                  SKU = sl.SkuId,
                                  ProductName = pm != null ? pm.ProductName : string.Empty,
                                  WarehouseName = sl.WarehouseId,
                                  Status = sl.Status,
                                  Quantity = sl.Quantity
                              }).ToListAsync();
            return Json(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllInboundLogs()
        {
            var list = await _db.StockMovements.AsNoTracking().Where(m => m.MovementType == "INBOUND").OrderByDescending(m => m.CreatedAt).Select(m => new {
                m.MovementId,
                m.SkuId,
                m.QuantityDelta,
                m.PerformedBy,
                CreatedAt = m.CreatedAt,
                Reference = m.ReferenceId ?? m.ReferenceType
            }).ToListAsync();
            return Json(list);
        }

        [HttpGet]
        public async Task<IActionResult> GetAllOutboundLogs()
        {
            var list = await _db.StockMovements.AsNoTracking().Where(m => m.MovementType == "OUTBOUND").OrderByDescending(m => m.CreatedAt).Select(m => new {
                m.MovementId,
                m.SkuId,
                m.QuantityDelta,
                m.PerformedBy,
                CreatedAt = m.CreatedAt,
                Reference = m.ReferenceId ?? m.ReferenceType
            }).ToListAsync();
            return Json(list);
        }

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Maintenance()
        {
            return View();
        }
    }
}
