using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
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
    public class StockTransferController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IInventoryRepository _repo;

        public StockTransferController(ApplicationDbContext db, IInventoryRepository repo)
        {
            _db = db;
            _repo = repo;
        }

        private async Task<List<Models.ViewModels.SkuDto>> GetSkuOptionsAsync()
        {
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
            var comboProducts = await _db.ComboProducts.AsNoTracking().ToListAsync();

            var list = skus.Select(s => new Models.ViewModels.SkuDto
            {
                SkuId = s.SkuId,
                ProductName = pmMap.FirstOrDefault(p => p.ProductModelId == s.ProductModelId)?.ProductName ?? string.Empty,
                Unit = "Cái"
            }).ToList();

            list.AddRange(comboProducts.Select(c => new Models.ViewModels.SkuDto
            {
                SkuId = c.ComboId,
                ProductName = "[COMBO] " + c.ComboName,
                Unit = "Bộ"
            }));

            return list.OrderBy(x => x.SkuId).ToList();
        }

        public async Task<IActionResult> Index()
        {
            var vouchers = await _db.InventoryVouchers
                .Where(v => v.Type == "TRANSFER")
                .OrderByDescending(v => v.Date)
                .ToListAsync();
            return View("List", vouchers); // We will create a List view for it.
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? id = null)
        {
            var vm = new Models.ViewModels.VoucherPageViewModel();
            vm.Skus = await GetSkuOptionsAsync();
            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            vm.Recent = new List<InventoryVoucher>();
            
            if (!string.IsNullOrWhiteSpace(id))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id && v.Type == "TRANSFER");
                if (existing != null)
                {
                    vm.Form = new Models.ViewModels.VoucherFormViewModel
                    {
                        VoucherId = existing.VoucherId,
                        VoucherNumber = existing.VoucherNumber,
                        Date = existing.Date,
                        Counterparty = existing.Counterparty ?? string.Empty,
                        Address = existing.Address ?? string.Empty,
                        Reason = existing.Reason ?? string.Empty,
                        ReferenceDocument = existing.ReferenceDocument ?? string.Empty,
                        WarehouseId = existing.WarehouseId ?? string.Empty,
                        Lines = existing.Lines.Select(l => new Models.ViewModels.VoucherLineInput
                        {
                            Id = l.Id,
                            SkuId = l.SkuId,
                            ProductName = l.ProductName ?? string.Empty,
                            Unit = l.Unit ?? "Cái",
                            Quantity = l.Quantity,
                            DocumentQuantity = l.DocumentQuantity,
                            BatchNumber = l.BatchNumber ?? string.Empty,
                            ExpiryDate = l.ExpiryDate,
                            UnitPrice = l.UnitPrice
                        }).ToList()
                    };
                }
                else
                {
                    vm.Form = new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };
                }
            }
            else
            {
                vm.Form = new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };
            }

            return View("Create", vm); // Outbound uses Create.cshtml as its create form
        }

        private async Task<Dictionary<string, int>> FlattenLinesAsync(IEnumerable<Models.ViewModels.VoucherLineInput> lines)
        {
            var dict = new Dictionary<string, int>();
            foreach (var l in lines)
            {
                if (string.IsNullOrWhiteSpace(l.SkuId) || l.Quantity <= 0) continue;
                var combo = await _db.ComboProducts.Include(c => c.Components).FirstOrDefaultAsync(c => c.ComboId == l.SkuId);
                if (combo != null && combo.Components != null && combo.Components.Any())
                {
                    foreach (var bom in combo.Components)
                    {
                        var qty = l.Quantity * bom.QuantityPerSet;
                        if (dict.ContainsKey(bom.SkuId)) dict[bom.SkuId] += qty;
                        else dict[bom.SkuId] = qty;
                    }
                }
                else
                {
                    if (dict.ContainsKey(l.SkuId)) dict[l.SkuId] += l.Quantity;
                    else dict[l.SkuId] = l.Quantity;
                }
            }
            return dict;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] Models.ViewModels.VoucherFormViewModel form)
        {
            ModelState.Remove("VoucherId");
            ModelState.Remove("VoucherNumber");

            if (!ModelState.IsValid)
            {
                var vm = new Models.ViewModels.VoucherPageViewModel();
                vm.Skus = await GetSkuOptionsAsync();
                vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
                vm.Recent = new List<InventoryVoucher>();
                vm.Form = form ?? new Models.ViewModels.VoucherFormViewModel { Lines = new List<Models.ViewModels.VoucherLineInput>() };
                return View("Create", vm);
            }

            if (form == null || string.IsNullOrWhiteSpace(form.WarehouseId) || form.Lines == null || !form.Lines.Any())
            {
                TempData["Error"] = "Dữ liệu phiếu không hợp lệ.";
                return RedirectToAction("Index");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(form.VoucherId))
                {
                    // UPDATE DRAFT FLOW
                    var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == form.VoucherId);
                    if (existing == null) { TempData["Error"] = "Phiếu không tồn tại."; return RedirectToAction("Index"); }
                    if (existing.IsLocked || existing.Status == "APPROVED")
                    {
                        TempData["Error"] = "Phiếu đã bị khóa hoặc đã duyệt, không thể chỉnh sửa.";
                        await transaction.RollbackAsync();
                        return RedirectToAction("Index");
                    }

                    existing.VoucherNumber = string.IsNullOrWhiteSpace(form.VoucherNumber) ? existing.VoucherNumber : form.VoucherNumber;
                    existing.Date = form.Date;
                    existing.Counterparty = form.Counterparty;
                    existing.Address = form.Address;
                    existing.Reason = form.Reason;
                    existing.ReferenceDocument = form.ReferenceDocument ?? string.Empty;
                    existing.WarehouseId = form.WarehouseId;

                    _db.VoucherLines.RemoveRange(existing.Lines);
                    existing.Lines.Clear();
                    decimal total = 0m;
                    foreach (var line in form.Lines ?? new List<Models.ViewModels.VoucherLineInput>())
                    {
                        if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;
                        var ln = new VoucherLine { SkuId = line.SkuId, SkuCode = line.SkuId, ProductName = line.ProductName ?? "", Unit = line.Unit ?? "", Quantity = line.Quantity, DocumentQuantity = line.DocumentQuantity, BatchNumber = line.BatchNumber ?? string.Empty, ExpiryDate = line.ExpiryDate, UnitPrice = line.UnitPrice, LineTotal = line.UnitPrice * line.Quantity, VoucherId = existing.VoucherId };
                        total += ln.LineTotal;
                        existing.Lines.Add(ln);
                    }
                    existing.TotalAmount = total;
                    
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    TempData["Message"] = "Cập nhật phiếu xuất (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
                else
                {
                    // CREATE DRAFT FLOW
                    var voucher = new InventoryVoucher
                    {
                        VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", StringComparison.OrdinalIgnoreCase)) ? ("CK-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.VoucherNumber,
                        Date = form.Date,
                        Type = "TRANSFER",
                        Status = "DRAFT",
                        Counterparty = form.Counterparty,
                        Address = form.Address,
                        Reason = form.Reason,
                        ReferenceDocument = form.ReferenceDocument ?? string.Empty,
                        WarehouseId = form.WarehouseId,
                        CreatedBy = User?.Identity?.Name
                    };
                    
                    decimal total = 0m;
                    foreach (var line in form.Lines ?? new List<Models.ViewModels.VoucherLineInput>())
                    {
                        if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;
                        var ln = new VoucherLine
                        {
                            SkuId = line.SkuId, SkuCode = line.SkuId, ProductName = line.ProductName ?? string.Empty, Unit = line.Unit ?? "Cái", Quantity = line.Quantity, DocumentQuantity = line.DocumentQuantity, BatchNumber = line.BatchNumber ?? string.Empty, ExpiryDate = line.ExpiryDate, UnitPrice = line.UnitPrice, LineTotal = line.UnitPrice * line.Quantity
                        };
                        total += ln.LineTotal;
                        voucher.Lines.Add(ln);
                    }
                    voucher.TotalAmount = total;
                    _db.InventoryVouchers.Add(voucher);
                    await _db.SaveChangesAsync();
                    await transaction.CommitAsync();
                    TempData["Message"] = "Tạo phiếu xuất (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
            }

            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[StockTransferController Error]: {ex}");
                TempData["Error"] = "Đã xảy ra lỗi hệ thống khi xử lý.";
                return RedirectToAction("Index");
            }
        }
    
        [HttpPost]
        public async Task<IActionResult> Approve(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();

            var voucher = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id);
            if (voucher == null) { TempData["Error"] = "Phiếu không tồn tại."; return RedirectToAction("Index"); }

            if (voucher.Status != "DRAFT" && voucher.Status != "PENDING")
            {
                TempData["Error"] = "Phiếu đã duyệt hoặc không hợp lệ.";
                return RedirectToAction("Index");
            }

            // BR-02: Phiếu chuyển kho chỉ được trình duyệt khi có lệnh chuyển kho hợp lệ từ bộ phận/đơn vị nhận
            if (string.IsNullOrWhiteSpace(voucher.ReferenceDocument))
            {
                TempData["Error"] = "Lỗi BR-02: Phiếu chuyển kho chỉ được duyệt khi có Yêu cầu xuất hợp lệ (Chứng từ gốc).";
                return RedirectToAction("Index");
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var skusToLock = voucher.Lines.Select(l => l.SkuId).Distinct().ToList();
                if (string.IsNullOrWhiteSpace(voucher.ToWarehouseId))
                {
                    TempData["Error"] = "Lỗi: Phiếu chuyển kho phải có Kho Đích.";
                    await transaction.RollbackAsync();
                    return RedirectToAction("Index");
                }
                
                if (voucher.WarehouseId == voucher.ToWarehouseId)
                {
                    TempData["Error"] = "Lỗi: Kho Nguồn và Kho Đích không được trùng nhau.";
                    await transaction.RollbackAsync();
                    return RedirectToAction("Index");
                }

                // BR-05: Khóa cả bản ghi nguồn và đích; kiểm tra đủ tồn nguồn; cập nhật hai phía trước commit.
                var lockedSourceLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, voucher.WarehouseId, "GOOD");
                var lockedDestLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, voucher.ToWarehouseId, "GOOD");

                // Check Overstock (vượt tồn Nguồn)
                foreach (var line in voucher.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;
                    
                    var sourceLedger = lockedSourceLedgers.FirstOrDefault(l => l.SkuId == line.SkuId && l.BatchNumber == (line.BatchNumber ?? string.Empty));
                    if (sourceLedger == null || sourceLedger.Quantity < line.Quantity)
                    {
                        TempData["Error"] = $"BR-05: Không đủ tồn kho cho sản phẩm {line.ProductName} (Lô: {line.BatchNumber}) tại Kho Nguồn. Tồn hiện tại: {sourceLedger?.Quantity ?? 0}, Yêu cầu chuyển: {line.Quantity}";
                        await transaction.RollbackAsync();
                        return RedirectToAction("Index");
                    }
                }

                // Apply deduction (Source) & addition (Destination)
                foreach (var line in voucher.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;
                    
                    // Giảm Tồn kho nguồn
                    var sourceLedger = lockedSourceLedgers.First(l => l.SkuId == line.SkuId && l.BatchNumber == (line.BatchNumber ?? string.Empty));
                    sourceLedger.Quantity -= line.Quantity;
                    await _repo.UpdateLedgerAsync(sourceLedger);

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = line.SkuId,
                        WarehouseId = voucher.WarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = -line.Quantity,
                        MovementType = "TRANSFER_OUT",
                        ReferenceType = "VOUCHER",
                        ReferenceId = voucher.VoucherId,
                        PerformedBy = User?.Identity?.Name,
                        CreatedAt = DateTime.UtcNow
                    });

                    // Tăng Tồn kho đích
                    var destLedger = lockedDestLedgers.FirstOrDefault(l => l.SkuId == line.SkuId && l.BatchNumber == (line.BatchNumber ?? string.Empty));
                    if (destLedger == null)
                    {
                        destLedger = new StockLedger { SkuId = line.SkuId, WarehouseId = voucher.ToWarehouseId, Status = "GOOD", Quantity = 0, BatchNumber = line.BatchNumber ?? string.Empty, ExpiryDate = line.ExpiryDate };
                        await _repo.AddLedgerAsync(destLedger);
                        lockedDestLedgers.Add(destLedger);
                    }
                    destLedger.Quantity += line.Quantity;
                    await _repo.UpdateLedgerAsync(destLedger);

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = line.SkuId,
                        WarehouseId = voucher.ToWarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = line.Quantity,
                        MovementType = "TRANSFER_IN",
                        ReferenceType = "VOUCHER",
                        ReferenceId = voucher.VoucherId,
                        PerformedBy = User?.Identity?.Name,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                voucher.Status = "APPROVED";
                voucher.IsLocked = true;
                voucher.ApprovedBy = User?.Identity?.Name;
                
                // Cập nhật lại ngày chứng từ thực xuất
                voucher.Date = DateTime.UtcNow;

                // BR-08: Ghi AuditLog
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityType = "InventoryVoucher",
                    EntityId = voucher.VoucherId,
                    Action = "APPROVE",
                    PerformedBy = User?.Identity?.Name ?? "System",
                    Reason = "Duyệt phiếu chuyển kho"
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Message"] = "Duyệt phiếu chuyển kho thành công! Tồn kho đã được trừ.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[StockTransferController Approve Error]: {ex}");
                TempData["Error"] = "Lỗi khi duyệt phiếu.";
            }

            return RedirectToAction("Index");
        }

}
}
