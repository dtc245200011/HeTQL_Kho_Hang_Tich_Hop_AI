using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class OutboundController : Controller
    {
        private readonly ApplicationDbContext _db;
        public OutboundController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            var vm = new Models.ViewModels.VoucherPageViewModel();

            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
            vm.Skus = skus.Select(s => new Models.ViewModels.SkuDto
            {
                SkuId = s.SkuId,
                ProductName = pmMap.FirstOrDefault(p => p.ProductModelId == s.ProductModelId)?.ProductName ?? string.Empty,
                Unit = "Cái"
            }).ToList();

            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            // Do not load recent history here - keep view focused on creating vouchers
            vm.Recent = new List<InventoryVoucher>();
            vm.Form = new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Provide model and render the Index view which contains the voucher form
            var vm = new Models.ViewModels.VoucherPageViewModel();
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
            vm.Skus = skus.Select(s => new Models.ViewModels.SkuDto
            {
                SkuId = s.SkuId,
                ProductName = pmMap.FirstOrDefault(p => p.ProductModelId == s.ProductModelId)?.ProductName ?? string.Empty,
                Unit = "Cái"
            }).ToList();
            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            // Recent history omitted
            vm.Recent = new List<InventoryVoucher>();
            vm.Form = new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };
            // Render the Index view, which contains the voucher UI
            return View("Index", vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] Models.ViewModels.VoucherFormViewModel form)
        {
            // Remove server-generated fields from validation so missing VoucherId/Number won't block create
            ModelState.Remove("VoucherId");
            ModelState.Remove("VoucherNumber");

            if (!ModelState.IsValid)
            {
                var errors = string.Join("; ", ModelState.Values.SelectMany(x => x.Errors).Select(x => x.ErrorMessage));
                Console.WriteLine($"[CREATE OUTBOUND ERROR]: {errors}");

                // Rebuild page view model so the form can be re-rendered with validation messages
                var vm = new Models.ViewModels.VoucherPageViewModel();
                var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
                var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
                vm.Skus = skus.Select(s => new Models.ViewModels.SkuDto
                {
                    SkuId = s.SkuId,
                    ProductName = pmMap.FirstOrDefault(p => p.ProductModelId == s.ProductModelId)?.ProductName ?? string.Empty,
                    Unit = "Cái"
                }).ToList();
                vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
                vm.Recent = new List<InventoryVoucher>();
                vm.Form = form ?? new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };
                return View(vm);
            }

            // Update flow if VoucherId provided
            if (!string.IsNullOrWhiteSpace(form.VoucherId))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == form.VoucherId);
                if (existing == null) { TempData["Error"] = "Phiếu không tồn tại."; return RedirectToAction("Index"); }

                existing.VoucherNumber = string.IsNullOrWhiteSpace(form.VoucherNumber) ? existing.VoucherNumber : form.VoucherNumber;
                existing.Date = form.Date;
                existing.Counterparty = form.Counterparty;
                existing.Address = form.Address;
                existing.Reason = form.Reason;
                var oldWarehouse = existing.WarehouseId;
                existing.WarehouseId = form.WarehouseId;

                var posted = form.Lines ?? new List<Models.ViewModels.VoucherLineInput>();
                var toRemove = existing.Lines.Where(el => !posted.Any(p => p.Id.HasValue && p.Id.Value == el.Id)).ToList();
                foreach (var rem in toRemove)
                {
                    var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == rem.SkuId && l.WarehouseId == oldWarehouse && l.Status == "GOOD");
                    if (ledger != null) ledger.Quantity += rem.Quantity; // rollback outbound
                    _db.VoucherLines.Remove(rem);
                }

                foreach (var p in posted)
                {
                    if (p.Id.HasValue)
                    {
                        var exLine = existing.Lines.FirstOrDefault(x => x.Id == p.Id.Value);
                        if (exLine != null)
                        {
                            var delta = p.Quantity - exLine.Quantity;
                            // for outbound, positive delta means more items being removed
                            if (delta > 0)
                            {
                                var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == exLine.SkuId && l.WarehouseId == existing.WarehouseId && l.Status == "GOOD");
                                if (ledger == null || ledger.Quantity < delta)
                                {
                                    TempData["Error"] = $"Không đủ tồn cho SKU {exLine.SkuId} khi cập nhật.";
                                    return RedirectToAction("Index");
                                }
                                ledger.Quantity -= delta;
                            }
                            else if (delta < 0)
                            {
                                var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == exLine.SkuId && l.WarehouseId == existing.WarehouseId && l.Status == "GOOD");
                                if (ledger != null) ledger.Quantity -= delta * -1 * -1; // increase by -delta
                            }

                            exLine.SkuId = p.SkuId;
                            exLine.ProductName = p.ProductName;
                            exLine.Unit = p.Unit;
                            exLine.UnitPrice = p.UnitPrice;
                            exLine.Quantity = p.Quantity;
                            exLine.LineTotal = p.UnitPrice * p.Quantity;

                            _db.StockMovements.Add(new StockMovement { SkuId = exLine.SkuId, WarehouseId = existing.WarehouseId, StockStatus = "GOOD", QuantityDelta = -delta, MovementType = "OUTBOUND", ReferenceType = "VOUCHER", ReferenceId = existing.VoucherId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow });
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(p.SkuId) || p.Quantity <= 0) continue;
                        var ln = new VoucherLine { SkuId = p.SkuId, SkuCode = p.SkuId, ProductName = p.ProductName, Unit = p.Unit, Quantity = p.Quantity, UnitPrice = p.UnitPrice, LineTotal = p.UnitPrice * p.Quantity, VoucherId = existing.VoucherId };
                        existing.Lines.Add(ln);

                        var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == ln.SkuId && l.WarehouseId == existing.WarehouseId && l.Status == "GOOD");
                        if (ledger == null || ledger.Quantity < ln.Quantity)
                        {
                            TempData["Error"] = $"Không đủ tồn cho SKU {ln.SkuId} trong kho {existing.WarehouseId}.";
                            return RedirectToAction("Index");
                        }
                        ledger.Quantity -= ln.Quantity;

                        _db.StockMovements.Add(new StockMovement { SkuId = ln.SkuId, WarehouseId = existing.WarehouseId, StockStatus = "GOOD", QuantityDelta = -ln.Quantity, MovementType = "OUTBOUND", ReferenceType = "VOUCHER", ReferenceId = existing.VoucherId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow });
                    }
                }

                existing.TotalAmount = existing.Lines.Sum(x => x.LineTotal);
                await _db.SaveChangesAsync();
                TempData["Message"] = "Cập nhật phiếu xuất thành công.";
                return RedirectToAction("Index");
            }
            if (form == null || string.IsNullOrWhiteSpace(form.WarehouseId) || form.Lines == null || !form.Lines.Any())
            {
                TempData["Error"] = "Dữ liệu phiếu không hợp lệ.";
                return RedirectToAction("Index");
            }

            var voucher = new InventoryVoucher
            {
                VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", System.StringComparison.OrdinalIgnoreCase)) ? $"PX-{DateTime.UtcNow:yyyyMMdd}-{Guid.NewGuid().ToString().Substring(0,4).ToUpper()}" : form.VoucherNumber,
                Date = form.Date,
                Type = "OUTBOUND",
                Counterparty = form.Counterparty,
                Address = form.Address,
                Reason = form.Reason,
                WarehouseId = form.WarehouseId
            };

            decimal total = 0m;
            foreach (var line in form.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;
                var ln = new VoucherLine
                {
                    SkuId = line.SkuId,
                    SkuCode = line.SkuId,
                    ProductName = line.ProductName ?? string.Empty,
                    Unit = line.Unit ?? "Cái",
                    Quantity = line.Quantity,
                    UnitPrice = line.UnitPrice,
                    LineTotal = line.UnitPrice * line.Quantity
                };
                total += ln.LineTotal;
                voucher.Lines.Add(ln);

                // update ledger
                var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == ln.SkuId && l.WarehouseId == form.WarehouseId && l.Status == "GOOD");
                if (ledger == null || ledger.Quantity < ln.Quantity)
                {
                    TempData["Error"] = $"Không đủ tồn cho SKU {ln.SkuId} trong kho {form.WarehouseId}.";
                    return RedirectToAction("Index");
                }
                ledger.Quantity -= ln.Quantity;

                // stock movement
                var mv = new StockMovement
                {
                    SkuId = ln.SkuId,
                    WarehouseId = form.WarehouseId,
                    StockStatus = "GOOD",
                    QuantityDelta = -ln.Quantity,
                    MovementType = "OUTBOUND",
                    ReferenceType = "VOUCHER",
                    ReferenceId = voucher.VoucherId,
                    PerformedBy = User?.Identity?.Name,
                    CreatedAt = DateTime.UtcNow
                };
                _db.StockMovements.Add(mv);
            }

            voucher.TotalAmount = total;
            _db.InventoryVouchers.Add(voucher);
            await _db.SaveChangesAsync();

            TempData["Message"] = "Lưu phiếu xuất thành công.";
            return RedirectToAction("Index");
        }
    }
}
