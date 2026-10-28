using DuAnCode.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DuAnCode.Web.Controllers
{
    [Authorize]
    public class QuickSearchController : Controller
    {
        private readonly ApplicationDbContext _db;

        public QuickSearchController(ApplicationDbContext db)
        {
            _db = db;
        }

        [HttpGet]
        [Route("api/quick-search")]
        public async Task<IActionResult> Search([FromQuery] string? q)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 1)
            {
                return Ok(new { products = new List<object>(), vouchers = new List<object>() });
            }

            var query = q.Trim();

            // 1. Search Products / SKUs
            var matchingSkus = await _db.SkuVariants.AsNoTracking()
                .Where(s => s.SkuId.Contains(query) || 
                            (s.Color != null && s.Color.Contains(query)) || 
                            (s.Material != null && s.Material.Contains(query)))
                .Take(5)
                .ToListAsync();

            var matchingPmIds = await _db.ProductModels.AsNoTracking()
                .Where(p => p.ProductName.Contains(query) || (p.Category != null && p.Category.Contains(query)))
                .Select(p => p.ProductModelId)
                .Take(5)
                .ToListAsync();

            if (matchingPmIds.Any())
            {
                var skusFromPm = await _db.SkuVariants.AsNoTracking()
                    .Where(s => matchingPmIds.Contains(s.ProductModelId) && !matchingSkus.Select(m => m.SkuId).Contains(s.SkuId))
                    .Take(5 - matchingSkus.Count)
                    .ToListAsync();
                matchingSkus.AddRange(skusFromPm);
            }

            var skuIds = matchingSkus.Select(s => s.SkuId).ToList();
            var pmMap = await _db.ProductModels.AsNoTracking()
                .Where(p => matchingSkus.Select(s => s.ProductModelId).Contains(p.ProductModelId))
                .ToDictionaryAsync(p => p.ProductModelId, p => p.ProductName);

            var stockLedgers = await _db.StockLedgers.AsNoTracking()
                .Where(sl => skuIds.Contains(sl.SkuId))
                .ToListAsync();

            var stockMap = stockLedgers
                .GroupBy(sl => sl.SkuId)
                .ToDictionary(
                    g => g.Key,
                    g => new { TotalQty = g.Sum(x => x.Quantity), Warehouses = string.Join(", ", g.Select(x => x.WarehouseId).Distinct()) }
                );

            var productResults = matchingSkus.Select(s =>
            {
                var productName = pmMap.TryGetValue(s.ProductModelId, out var name) ? name : s.SkuId;
                var stock = stockMap.TryGetValue(s.SkuId, out var st) ? $"{st.TotalQty} cái ({st.Warehouses})" : "0 cái";
                var colorMat = !string.IsNullOrEmpty(s.Color) || !string.IsNullOrEmpty(s.Material) ? $" • {(s.Color ?? "")} {(s.Material ?? "")}".Trim() : "";
                return new
                {
                    skuId = s.SkuId,
                    title = $"{s.SkuId} - {productName}",
                    subtitle = $"Tồn kho: {stock}{colorMat}",
                    url = $"/Home/Index?tab=inventory&search={Uri.EscapeDataString(s.SkuId)}"
                };
            }).ToList();

            // 2. Search Vouchers (Inbound, Outbound, Transfer)
            var matchingVouchers = await _db.InventoryVouchers.AsNoTracking()
                .Where(v => v.VoucherNumber.Contains(query) || 
                            (v.CreatedBy != null && v.CreatedBy.Contains(query)) || 
                            (v.ReferenceDocument != null && v.ReferenceDocument.Contains(query)) ||
                            (v.Reason != null && v.Reason.Contains(query)))
                .OrderByDescending(v => v.Date)
                .Take(6)
                .ToListAsync();

            // Also search Audits
            var matchingAudits = await _db.InventoryAudits.AsNoTracking()
                .Where(a => a.AuditNumber.Contains(query) || 
                            (a.CreatedBy != null && a.CreatedBy.Contains(query)) || 
                            (a.WarehouseId != null && a.WarehouseId.Contains(query)) ||
                            (a.Reason != null && a.Reason.Contains(query)))
                .OrderByDescending(a => a.Date)
                .Take(3)
                .ToListAsync();

            var voucherResults = matchingVouchers.Select(v =>
            {
                var typeLabel = v.Type switch
                {
                    "INBOUND" => "Phiếu Nhập kho",
                    "OUTBOUND" => "Phiếu Xuất kho",
                    "TRANSFER" => "Phiếu Chuyển kho",
                    _ => "Chứng từ"
                };

                var url = v.Type switch
                {
                    "INBOUND" => $"/Inbound/Create?id={v.VoucherId}",
                    "OUTBOUND" => $"/Outbound/Create?id={v.VoucherId}",
                    "TRANSFER" => $"/StockTransfer/Create?id={v.VoucherId}",
                    _ => "/Home/Index"
                };

                var statusBadge = v.Status switch
                {
                    "APPROVED" => "Đã duyệt",
                    "PENDING" => "Chờ duyệt",
                    "REJECTED" => "Từ chối",
                    _ => "Nháp"
                };

                return new
                {
                    number = v.VoucherNumber,
                    type = typeLabel,
                    status = statusBadge,
                    statusCode = v.Status,
                    subtitle = $"{v.Date.ToLocalTime():dd/MM/yyyy HH:mm} • Người lập: {v.CreatedBy ?? "N/A"} • {v.TotalAmount:C0}",
                    url = url
                };
            }).ToList();

            voucherResults.AddRange(matchingAudits.Select(a =>
            {
                var statusBadge = a.Status switch
                {
                    "APPROVED" => "Đã duyệt",
                    "PENDING" => "Chờ duyệt",
                    "REJECTED" => "Từ chối",
                    _ => "Nháp"
                };

                return new
                {
                    number = a.AuditNumber,
                    type = "Phiếu Kiểm kê",
                    status = statusBadge,
                    statusCode = a.Status,
                    subtitle = $"{a.Date.ToLocalTime():dd/MM/yyyy HH:mm} • Người lập: {a.CreatedBy ?? "N/A"} • Kho: {a.WarehouseId ?? "N/A"}",
                    url = $"/InventoryAudit/Create?id={a.AuditId}"
                };
            }));

            return Ok(new
            {
                products = productResults,
                vouchers = voucherResults
            });
        }

        [HttpGet]
        [Route("api/stock/available")]
        public async Task<IActionResult> GetAvailableStock([FromQuery] string? warehouseId, [FromQuery] string? skuId)
        {
            var config = await _db.SystemConfigs.AsNoTracking().FirstOrDefaultAsync();
            if (config?.StopApi == true && !User.IsInRole("Admin") && !User.IsInRole("Quản trị viên"))
            {
                return StatusCode(503, new { ok = false, message = "Cổng API kết nối từ bên thứ ba hiện đang tạm khóa bởi Quản trị viên hệ thống." });
            }

            if (string.IsNullOrWhiteSpace(warehouseId))
                return BadRequest(new { message = "warehouseId is required" });

            var query = _db.StockLedgers.AsNoTracking()
                .Where(sl => sl.WarehouseId == warehouseId && sl.Status == "GOOD");

            if (!string.IsNullOrWhiteSpace(skuId))
            {
                var total = await query.Where(sl => sl.SkuId == skuId).SumAsync(sl => sl.Quantity);
                return Ok(new { warehouseId, skuId, available = total });
            }

            var stockMap = await query
                .GroupBy(sl => sl.SkuId)
                .Select(g => new { SkuId = g.Key, Available = g.Sum(x => x.Quantity) })
                .ToDictionaryAsync(x => x.SkuId, x => x.Available);

            return Ok(new { warehouseId, stock = stockMap });
        }

        [HttpGet]
        [Route("api/stock/warehouse-snapshot")]
        public async Task<IActionResult> GetWarehouseSnapshot([FromQuery] string? warehouseId)
        {
            var config = await _db.SystemConfigs.AsNoTracking().FirstOrDefaultAsync();
            if (config?.StopApi == true && !User.IsInRole("Admin") && !User.IsInRole("Quản trị viên"))
            {
                return StatusCode(503, new { ok = false, message = "Cổng API kết nối từ bên thứ ba hiện đang tạm khóa bởi Quản trị viên hệ thống." });
            }

            if (string.IsNullOrWhiteSpace(warehouseId))
                return BadRequest(new { message = "warehouseId is required" });

            var ledgers = await _db.StockLedgers.AsNoTracking()
                .Where(sl => sl.WarehouseId == warehouseId && sl.Quantity > 0)
                .ToListAsync();

            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToDictionaryAsync(p => p.ProductModelId, p => p);

            var items = ledgers.Select(l =>
            {
                var sku = skus.FirstOrDefault(s => s.SkuId == l.SkuId);
                var pm = sku != null && pmMap.TryGetValue(sku.ProductModelId, out var p) ? p : null;
                return new
                {
                    skuId = l.SkuId,
                    productName = pm?.ProductName ?? l.SkuId,
                    unit = pm?.Unit ?? "Cái",
                    batchNumber = l.BatchNumber ?? string.Empty,
                    stockStatus = l.Status ?? "GOOD",
                    systemQuantity = l.Quantity
                };
            }).ToList();

            return Ok(new { warehouseId, items });
        }
    }
}
