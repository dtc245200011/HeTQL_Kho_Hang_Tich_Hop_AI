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
    public class InboundController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IInventoryRepository _repo;
        private readonly DuAnCode.Web.Services.IApprovalWorkflowService _workflow;

        public InboundController(ApplicationDbContext db, IInventoryRepository repo, DuAnCode.Web.Services.IApprovalWorkflowService workflow)
        {
            _db = db;
            _repo = repo;
            _workflow = workflow;
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
                .Where(v => v.Type == "INBOUND")
                .OrderByDescending(v => v.Date)
                .ToListAsync();
            return View("List", vouchers);
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? id = null)
        {
            var config = await _db.SystemConfigs.FirstOrDefaultAsync();
            if (config != null)
            {
                if (config.MaintenanceMode)
                {
                    TempData["Error"] = "Hệ thống đang bảo trì, không thể truy cập chức năng này.";
                    return RedirectToAction("Index", "Home");
                }
                if (config.StopInbound)
                {
                    TempData["Error"] = "Chức năng tạo Phiếu Nhập Kho đã bị tạm dừng bởi Quản trị viên.";
                    return RedirectToAction("Index");
                }
            }

            var vm = new Models.ViewModels.VoucherPageViewModel();
            vm.Skus = await GetSkuOptionsAsync();
            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            vm.Recent = new List<InventoryVoucher>();
            
            if (!string.IsNullOrWhiteSpace(id))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id && v.Type == "INBOUND");
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

            return View(vm);
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
        
        private async Task AutoCreateMissingSkus(IEnumerable<Models.ViewModels.VoucherLineInput> lines)
        {
            if (lines == null) return;
            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line.SkuId)) continue;
                
                // If it is a combo, skip
                if (await _db.ComboProducts.AnyAsync(c => c.ComboId == line.SkuId)) continue;
                
                // Check if SKU exists
                var exists = await _db.SkuVariants.AnyAsync(s => s.SkuId == line.SkuId);
                if (!exists)
                {
                    // Auto-create ProductModel and SkuVariant
                    var pm = new ProductModel 
                    { 
                        ProductModelId = line.SkuId, 
                        ProductName = string.IsNullOrWhiteSpace(line.ProductName) ? line.SkuId : line.ProductName, 
                        Unit = string.IsNullOrWhiteSpace(line.Unit) ? "Cái" : line.Unit 
                    };
                    _db.ProductModels.Add(pm);
                    
                    var sku = new SkuVariant 
                    { 
                        SkuId = line.SkuId, 
                        ProductModelId = pm.ProductModelId, 
                        UnitPrice = line.UnitPrice 
                    };
                    _db.SkuVariants.Add(sku);
                    
                    await _db.SaveChangesAsync();
                }
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([FromForm] Models.ViewModels.VoucherFormViewModel form)
        {
            var config = await _db.SystemConfigs.FirstOrDefaultAsync();
            if (config != null && (config.MaintenanceMode || config.StopInbound))
            {
                TempData["Error"] = "Chức năng tạo Phiếu Nhập Kho đã bị tạm dừng bởi Quản trị viên.";
                return RedirectToAction("Index");
            }

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
                await AutoCreateMissingSkus(form.Lines);
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
                    TempData["Message"] = "Cập nhật phiếu nhập (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
                else
                {
                    // CREATE DRAFT FLOW
                    var prefix = config?.InboundPrefix ?? "PN-";
                    var voucher = new InventoryVoucher
                    {
                        VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", StringComparison.OrdinalIgnoreCase)) ? (prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.VoucherNumber,
                        Date = form.Date,
                        Type = "INBOUND",
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
                    TempData["Message"] = "Tạo phiếu nhập (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
            }

            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[InboundController Error]: {ex}");
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

            // BR-01: Phiếu nhập chỉ được trình duyệt khi có chứng từ nhà cung cấp
            if (string.IsNullOrWhiteSpace(voucher.ReferenceDocument))
            {
                TempData["Error"] = "Lỗi BR-01: Phiếu nhập chỉ được duyệt khi có Chứng từ nhà cung cấp (Chứng từ gốc).";
                return RedirectToAction("Index");
            }

            // --- WORKFLOW LOGIC ---
            if (voucher.Status == "DRAFT")
            {
                await _workflow.EnsureApprovalStepsForVoucherAsync(voucher);
                voucher.Status = "PENDING";
                await _db.SaveChangesAsync();
            }

            var steps = await _db.ApprovalSteps.Where(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
            var currentStep = steps.FirstOrDefault(s => s.Status == "PENDING");
            
            if (currentStep != null)
            {
                if (!User.IsInRole(currentStep.ApproverRole) && !User.IsInRole("Admin"))
                {
                    TempData["Error"] = $"Bạn không có quyền duyệt phiếu này. Đang chờ chữ ký của: {currentStep.ApproverRole}.";
                    return RedirectToAction("Index");
                }

                currentStep.Status = "APPROVED";
                currentStep.ApprovedBy = User.Identity?.Name;
                currentStep.ApprovedAt = DateTime.UtcNow;
                
                _db.AuditLogs.Add(new AuditLog { EntityType = "InventoryVoucher", EntityId = voucher.VoucherId, Action = $"APPROVE_LEVEL_{currentStep.Level}", PerformedBy = User.Identity?.Name ?? "System", Reason = $"Duyệt phiếu cấp {currentStep.Level} ({currentStep.ApproverRole})" });
                await _db.SaveChangesAsync();

                var nextStep = steps.FirstOrDefault(s => s.Status == "PENDING");
                if (nextStep != null)
                {
                    TempData["Message"] = $"Đã ký duyệt cấp {currentStep.Level} thành công. Chờ {nextStep.ApproverRole} duyệt tiếp.";
                    return RedirectToAction("Index");
                }
            }
            // --- END WORKFLOW LOGIC ---

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var skusToLock = voucher.Lines.Select(l => l.SkuId).Distinct().ToList();
                var lockedLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, voucher.WarehouseId, "GOOD");

                foreach (var line in voucher.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;

                    var ledger = lockedLedgers.FirstOrDefault(l => l.SkuId == line.SkuId && l.BatchNumber == (line.BatchNumber ?? string.Empty));
                    if (ledger == null)
                    {
                        ledger = new StockLedger { SkuId = line.SkuId, WarehouseId = voucher.WarehouseId, Status = "GOOD", Quantity = 0, BatchNumber = line.BatchNumber ?? string.Empty, ExpiryDate = line.ExpiryDate };
                        await _repo.AddLedgerAsync(ledger);
                        lockedLedgers.Add(ledger);
                    }
                    ledger.Quantity += line.Quantity;
                    await _repo.UpdateLedgerAsync(ledger);

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = line.SkuId,
                        WarehouseId = voucher.WarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = line.Quantity,
                        MovementType = "INBOUND",
                        ReferenceType = "VOUCHER",
                        ReferenceId = voucher.VoucherId,
                        PerformedBy = User?.Identity?.Name,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                voucher.Status = "APPROVED";
                voucher.IsLocked = true;
                voucher.ApprovedBy = User?.Identity?.Name;
                
                // Cập nhật lại ngày chứng từ thực nhập
                voucher.Date = DateTime.UtcNow;

                // BR-08: Ghi AuditLog
                _db.AuditLogs.Add(new AuditLog
                {
                    EntityType = "InventoryVoucher",
                    EntityId = voucher.VoucherId,
                    Action = "APPROVE",
                    PerformedBy = User?.Identity?.Name ?? "System",
                    Reason = "Duyệt phiếu nhập kho"
                });

                await _db.SaveChangesAsync();
                await transaction.CommitAsync();

                TempData["Message"] = "Duyệt phiếu nhập kho thành công! Tồn kho đã được cập nhật.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[InboundController Approve Error]: {ex}");
                TempData["Error"] = "Lỗi khi duyệt phiếu.";
            }

            return RedirectToAction("Index");
        }

}
}
