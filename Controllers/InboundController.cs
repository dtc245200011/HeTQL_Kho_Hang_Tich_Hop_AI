using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    [Microsoft.AspNetCore.Authorization.Authorize]
    public class InboundController : Controller
    {
        private readonly ApplicationDbContext _db;
        public InboundController(ApplicationDbContext db) { _db = db; }

        public async Task<IActionResult> Index()
        {
            // Prepare a strongly-typed view model and ensure lists are non-null
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

            // Recent history intentionally omitted to avoid duplication with dashboard
            vm.Recent = new List<InventoryVoucher>();

            // Ensure Form is initialized
            vm.Form = new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };

            // Return the Create view which contains the voucher form
            return View("Create", vm);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            // Return same model as Index/Create view to support /Inbound/Create route
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
            var vm = new Models.ViewModels.VoucherPageViewModel();
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
                Console.WriteLine($"[CREATE INBOUND ERROR]: {errors}");

                // Rebuild page model to re-render form with validation errors and posted values
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
                return View("Create", vm);
            }

            // If VoucherId is present, perform update flow
            if (!string.IsNullOrWhiteSpace(form.VoucherId))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == form.VoucherId);
                if (existing == null)
                {
                    TempData["Error"] = "Phiếu không tồn tại.";
                    return RedirectToAction("Index");
                }

                // update header
                existing.VoucherNumber = string.IsNullOrWhiteSpace(form.VoucherNumber) ? existing.VoucherNumber : form.VoucherNumber;
                existing.Date = form.Date;
                existing.Counterparty = form.Counterparty;
                existing.Address = form.Address;
                existing.Reason = form.Reason;
                var oldWarehouse = existing.WarehouseId;
                existing.WarehouseId = form.WarehouseId;

                // synchronize lines
                var posted = form.Lines ?? new List<Models.ViewModels.VoucherLineInput>();
                // remove deleted lines
                var toRemove = existing.Lines.Where(el => !posted.Any(p => p.Id.HasValue && p.Id.Value == el.Id)).ToList();
                foreach (var rem in toRemove)
                {
                    // adjust ledger rollback
                    var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == rem.SkuId && l.WarehouseId == oldWarehouse && l.Status == "GOOD");
                    if (ledger != null) ledger.Quantity -= rem.Quantity;
                    _db.VoucherLines.Remove(rem);
                }

                // update existing and add new
                foreach (var p in posted)
                {
                    if (p.Id.HasValue)
                    {
                        var exLine = existing.Lines.FirstOrDefault(x => x.Id == p.Id.Value);
                        if (exLine != null)
                        {
                            var delta = p.Quantity - exLine.Quantity;
                            exLine.SkuId = p.SkuId;
                            exLine.ProductName = p.ProductName;
                            exLine.Unit = p.Unit;
                            exLine.UnitPrice = p.UnitPrice;
                            exLine.Quantity = p.Quantity;
                            exLine.LineTotal = p.UnitPrice * p.Quantity;

                            // adjust ledger
                            var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == exLine.SkuId && l.WarehouseId == existing.WarehouseId && l.Status == "GOOD");
                            if (ledger == null)
                            {
                                ledger = new StockLedger { SkuId = exLine.SkuId, WarehouseId = existing.WarehouseId, Status = "GOOD", Quantity = 0 };
                                _db.StockLedgers.Add(ledger);
                            }
                            ledger.Quantity += delta;

                            // add movement
                            _db.StockMovements.Add(new StockMovement { SkuId = exLine.SkuId, WarehouseId = existing.WarehouseId, StockStatus = "GOOD", QuantityDelta = delta, MovementType = "INBOUND", ReferenceType = "VOUCHER", ReferenceId = existing.VoucherId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow });
                        }
                    }
                    else
                    {
                        if (string.IsNullOrWhiteSpace(p.SkuId) || p.Quantity <= 0) continue;
                        var ln = new VoucherLine { SkuId = p.SkuId, SkuCode = p.SkuId, ProductName = p.ProductName, Unit = p.Unit, Quantity = p.Quantity, UnitPrice = p.UnitPrice, LineTotal = p.UnitPrice * p.Quantity, VoucherId = existing.VoucherId };
                        existing.Lines.Add(ln);

                        var ledger = await _db.StockLedgers.FirstOrDefaultAsync(l => l.SkuId == ln.SkuId && l.WarehouseId == existing.WarehouseId && l.Status == "GOOD");
                        if (ledger == null)
                        {
                            ledger = new StockLedger { SkuId = ln.SkuId, WarehouseId = existing.WarehouseId, Status = "GOOD", Quantity = 0 };
                            _db.StockLedgers.Add(ledger);
                        }
                        ledger.Quantity += ln.Quantity;

                        _db.StockMovements.Add(new StockMovement { SkuId = ln.SkuId, WarehouseId = existing.WarehouseId, StockStatus = "GOOD", QuantityDelta = ln.Quantity, MovementType = "INBOUND", ReferenceType = "VOUCHER", ReferenceId = existing.VoucherId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow });
                    }
                }

                // recalc total
                existing.TotalAmount = existing.Lines.Sum(x => x.LineTotal);
                await _db.SaveChangesAsync();
                TempData["Message"] = "Cập nhật phiếu nhập thành công.";
                return RedirectToAction("Index");
            }
            if (form == null || string.IsNullOrWhiteSpace(form.WarehouseId) || form.Lines == null || !form.Lines.Any())
            {
                TempData["Error"] = "Dữ liệu phiếu không hợp lệ.";
                return RedirectToAction("Index");
            }

            var voucher = new InventoryVoucher
            {
                VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", StringComparison.OrdinalIgnoreCase)) ? ("PN-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.VoucherNumber,
                Date = form.Date,
                Type = "INBOUND",
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
                if (ledger == null)
                {
                    ledger = new StockLedger { SkuId = ln.SkuId, WarehouseId = form.WarehouseId, Status = "GOOD", Quantity = 0 };
                    _db.StockLedgers.Add(ledger);
                }
                ledger.Quantity += ln.Quantity;

                // stock movement
                var mv = new StockMovement
                {
                    SkuId = ln.SkuId,
                    WarehouseId = form.WarehouseId,
                    StockStatus = "GOOD",
                    QuantityDelta = ln.Quantity,
                    MovementType = "INBOUND",
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

            TempData["Message"] = "Lưu phiếu nhập thành công.";
            return RedirectToAction("Index");
        }
    }
}
