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

        public async Task<IActionResult> Index(string? search = null, string? status = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _db.InventoryVouchers
                .Where(v => v.Type == "INBOUND")
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(v => v.VoucherNumber.Contains(s) || (v.CreatedBy != null && v.CreatedBy.Contains(s)) || (v.ReferenceDocument != null && v.ReferenceDocument.Contains(s)) || (v.Reason != null && v.Reason.Contains(s)) || (v.Counterparty != null && v.Counterparty.Contains(s)));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(v => v.Status == status);
            }

            if (fromDate.HasValue)
            {
                var start = fromDate.Value.Date;
                query = query.Where(v => v.Date >= start);
            }

            if (toDate.HasValue)
            {
                var end = toDate.Value.Date.AddDays(1).AddTicks(-1);
                query = query.Where(v => v.Date <= end);
            }

            var vouchers = await query
                .OrderByDescending(v => v.Date)
                .ToListAsync();

            var rejectedIds = vouchers.Where(v => v.Status == "REJECTED").Select(v => v.VoucherId).ToList();
            if (rejectedIds.Any())
            {
                var logs = await _db.AuditLogs
                    .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryVoucher" && rejectedIds.Contains(a.EntityId))
                    .OrderByDescending(a => a.PerformedAt)
                    .ToListAsync();
                ViewBag.RejectionLogs = logs.GroupBy(a => a.EntityId).ToDictionary(g => g.Key, g => g.First());
            }
            else
            {
                ViewBag.RejectionLogs = new Dictionary<string, AuditLog>();
            }

            ViewBag.WarehouseMap = await _db.Warehouses.AsNoTracking().ToDictionaryAsync(w => w.WarehouseId, w => w.WarehouseName);
            ViewBag.Search = search;
            ViewBag.Status = status;
            ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
            ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

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
                    var (originalReason, rejectionReason) = DuAnCode.Web.Services.RejectionHelper.ExtractReason(existing.Reason);
                    DateTime? rejectedAt = null;
                    string? rejectedBy = existing.ApprovedBy;
                    if (existing.Status == "REJECTED")
                    {
                        var rejectLog = await _db.AuditLogs
                            .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryVoucher" && a.EntityId == existing.VoucherId)
                            .OrderByDescending(a => a.PerformedAt)
                            .FirstOrDefaultAsync();
                        if (rejectLog != null)
                        {
                            rejectedAt = rejectLog.PerformedAt;
                            if (!string.IsNullOrWhiteSpace(rejectLog.PerformedBy))
                                rejectedBy = rejectLog.PerformedBy;
                        }
                    }

                    var defaultSup = await _db.Suppliers.FirstOrDefaultAsync(s => s.IsActive) ?? await _db.Suppliers.FirstOrDefaultAsync();
                    var counterparty = !string.IsNullOrWhiteSpace(existing.Counterparty) ? existing.Counterparty : (defaultSup?.Name ?? "NCC Nội Thất 1");
                    var address = !string.IsNullOrWhiteSpace(existing.Address) ? existing.Address : (defaultSup?.Address ?? "Kho hàng nhà cung cấp");
                    var warehouseId = !string.IsNullOrWhiteSpace(existing.WarehouseId) && vm.Warehouses.Any(w => w.WarehouseId == existing.WarehouseId && w.WarehouseType != "DAMAGED")
                        ? existing.WarehouseId
                        : (vm.Warehouses.FirstOrDefault(w => w.WarehouseType == "MAIN")?.WarehouseId ?? vm.Warehouses.FirstOrDefault()?.WarehouseId ?? existing.WarehouseId ?? "");

                    var skuVariantsMap = await _db.SkuVariants.AsNoTracking().ToDictionaryAsync(s => s.SkuId, s => s);

                    vm.Form = new Models.ViewModels.VoucherFormViewModel
                    {
                        VoucherId = existing.VoucherId,
                        VoucherNumber = existing.VoucherNumber,
                        Date = existing.Date,
                        Counterparty = counterparty,
                        Address = address,
                        Reason = string.IsNullOrWhiteSpace(originalReason) ? existing.Reason : originalReason,
                        ReferenceDocument = existing.ReferenceDocument ?? string.Empty,
                        WarehouseId = warehouseId,
                        Status = existing.Status,
                        IsLocked = existing.IsLocked,
                        CreatedBy = existing.CreatedBy,
                        ApprovedBy = rejectedBy,
                        RejectedAt = rejectedAt,
                        RejectionReason = rejectionReason,
                        OriginalReason = originalReason,
                        Lines = existing.Lines.Select(l =>
                        {
                            var skuOpt = vm.Skus.FirstOrDefault(s => s.SkuId == l.SkuId);
                            skuVariantsMap.TryGetValue(l.SkuId, out var skuVar);

                            var prodName = !string.IsNullOrWhiteSpace(l.ProductName) ? l.ProductName : (skuOpt?.ProductName ?? l.SkuId);
                            var unit = !string.IsNullOrWhiteSpace(l.Unit) ? l.Unit : (skuOpt?.Unit ?? "Cái");
                            var price = l.UnitPrice > 0 ? l.UnitPrice : (skuVar?.UnitPrice ?? 0m);

                            return new Models.ViewModels.VoucherLineInput
                            {
                                Id = l.Id,
                                SkuId = l.SkuId,
                                ProductName = prodName,
                                Unit = unit,
                                Quantity = l.Quantity,
                                DocumentQuantity = l.DocumentQuantity,
                                BatchNumber = string.IsNullOrWhiteSpace(l.BatchNumber) ? $"LÔ-AI-{existing.Date:yyyyMMdd}" : l.BatchNumber,
                                ExpiryDate = l.ExpiryDate,
                                UnitPrice = price
                            };
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
                    var pm = await _db.ProductModels.FirstOrDefaultAsync(p => p.ProductModelId == line.SkuId);
                    if (pm == null)
                    {
                        pm = new ProductModel 
                        { 
                            ProductModelId = line.SkuId, 
                            ProductName = string.IsNullOrWhiteSpace(line.ProductName) ? line.SkuId : line.ProductName, 
                            Unit = string.IsNullOrWhiteSpace(line.Unit) ? "Cái" : line.Unit 
                        };
                        _db.ProductModels.Add(pm);
                    }
                    else if (!string.IsNullOrWhiteSpace(line.ProductName))
                    {
                        pm.ProductName = line.ProductName;
                        if (!string.IsNullOrWhiteSpace(line.Unit)) pm.Unit = line.Unit;
                    }
                    
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
            ModelState.Remove("Form.VoucherId");
            ModelState.Remove("Form.VoucherNumber");
            ModelState.Remove("Date");
            ModelState.Remove("Form.Date");
            ModelState.Remove("CreatedBy");
            ModelState.Remove("Form.CreatedBy");

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
                var targetVoucherId = !string.IsNullOrWhiteSpace(form.VoucherId) ? form.VoucherId : (Request != null && Request.HasFormContentType ? Request.Form["VoucherId"].FirstOrDefault() : null);
                if (!string.IsNullOrWhiteSpace(targetVoucherId))
                {
                    // UPDATE DRAFT FLOW
                    var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == targetVoucherId);
                    if (existing == null) { TempData["Error"] = "Phiếu không tồn tại."; return RedirectToAction("Index"); }
                    if (existing.IsLocked || existing.Status == "APPROVED")
                    {
                        TempData["Error"] = "Phiếu đã bị khóa hoặc đã duyệt, không thể chỉnh sửa.";
                        await transaction.RollbackAsync();
                        return RedirectToAction("Index");
                    }

                    existing.VoucherNumber = string.IsNullOrWhiteSpace(form.VoucherNumber) ? existing.VoucherNumber : form.VoucherNumber;
                    existing.Date = form.Date ?? existing.Date;
                    existing.Counterparty = form.Counterparty;
                    existing.Address = form.Address;
                    existing.Reason = form.Reason;
                    existing.ReferenceDocument = form.ReferenceDocument ?? string.Empty;
                    existing.WarehouseId = form.WarehouseId;
                    if (!string.IsNullOrWhiteSpace(form.CreatedBy)) existing.CreatedBy = form.CreatedBy;

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
                        Date = form.Date ?? DateTime.UtcNow,
                        Type = "INBOUND",
                        Status = "DRAFT",
                        Counterparty = form.Counterparty,
                        Address = form.Address,
                        Reason = form.Reason,
                        ReferenceDocument = form.ReferenceDocument ?? string.Empty,
                        WarehouseId = form.WarehouseId,
                        CreatedBy = !string.IsNullOrWhiteSpace(form.CreatedBy) ? form.CreatedBy : (User?.Identity?.Name ?? "admin")
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
                if (!DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, currentStep.ApproverRole))
                {
                    TempData["Error"] = $"Bạn không có quyền duyệt phiếu này. Đang chờ chữ ký của: {DuAnCode.Web.Services.RoleHelper.ToVietnamese(currentStep.ApproverRole)}.";
                    return RedirectToAction("Index");
                }

                currentStep.Status = "APPROVED";
                currentStep.ApprovedBy = User.Identity?.Name;
                currentStep.ApprovedAt = DateTime.UtcNow;
                
                _db.AuditLogs.Add(new AuditLog { EntityType = "InventoryVoucher", EntityId = voucher.VoucherId, Action = $"APPROVE_LEVEL_{currentStep.Level}", PerformedBy = User.Identity?.Name ?? "System", Reason = $"Duyệt phiếu cấp {currentStep.Level} ({DuAnCode.Web.Services.RoleHelper.ToVietnamese(currentStep.ApproverRole)})" });
                await _db.SaveChangesAsync();

                var nextStep = steps.FirstOrDefault(s => s.Status == "PENDING");
                if (nextStep != null)
                {
                    TempData["Message"] = $"Đã ký duyệt cấp {currentStep.Level} thành công. Chờ {DuAnCode.Web.Services.RoleHelper.ToVietnamese(nextStep.ApproverRole)} duyệt tiếp.";
                    return RedirectToAction("Index");
                }
            }
            // --- END WORKFLOW LOGIC ---

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var comboIds = voucher.Lines.Select(l => l.SkuId).Distinct().ToList();
                var combos = await _db.ComboProducts.Include(c => c.Components).Where(c => comboIds.Contains(c.ComboId)).ToListAsync();

                // Flatten lines: explode Combos into BOM components
                var itemsToAdd = new List<(string SkuId, string BatchNumber, DateTime? ExpiryDate, int Quantity)>();
                foreach (var line in voucher.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;

                    var combo = combos.FirstOrDefault(c => c.ComboId == line.SkuId);
                    if (combo != null && combo.Components != null && combo.Components.Any())
                    {
                        foreach (var bom in combo.Components)
                        {
                            var compQty = line.Quantity * Math.Max(1, bom.QuantityPerSet);
                            itemsToAdd.Add((bom.SkuId, line.BatchNumber ?? string.Empty, line.ExpiryDate, compQty));
                        }
                    }
                    else
                    {
                        itemsToAdd.Add((line.SkuId, line.BatchNumber ?? string.Empty, line.ExpiryDate, line.Quantity));
                    }
                }

                var skusToLock = itemsToAdd.Select(i => i.SkuId).Distinct().OrderBy(s => s).ToList();
                var lockedLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, voucher.WarehouseId, "GOOD");

                foreach (var item in itemsToAdd)
                {
                    var ledger = lockedLedgers.FirstOrDefault(l => l.SkuId == item.SkuId && l.BatchNumber == item.BatchNumber);
                    if (ledger == null)
                    {
                        ledger = new StockLedger
                        {
                            SkuId = item.SkuId,
                            WarehouseId = voucher.WarehouseId,
                            Status = "GOOD",
                            Quantity = item.Quantity,
                            BatchNumber = item.BatchNumber,
                            ExpiryDate = item.ExpiryDate
                        };
                        await _repo.AddLedgerAsync(ledger);
                        lockedLedgers.Add(ledger);
                    }
                    else
                    {
                        ledger.Quantity += item.Quantity;
                        await _repo.UpdateLedgerAsync(ledger);
                    }

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = item.SkuId,
                        WarehouseId = voucher.WarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = item.Quantity,
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

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(string id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();

            var voucher = await _db.InventoryVouchers.FirstOrDefaultAsync(v => v.VoucherId == id);
            if (voucher == null) { TempData["Error"] = "Phiếu không tồn tại."; return RedirectToAction("Index"); }

            if (voucher.Status != "DRAFT" && voucher.Status != "PENDING")
            {
                TempData["Error"] = "Chỉ có thể từ chối phiếu đang ở trạng thái Nháp hoặc Chờ duyệt.";
                return RedirectToAction("Index");
            }

            var steps = await _db.ApprovalSteps.Where(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
            var currentStep = steps.FirstOrDefault(s => s.Status == "PENDING");
            if (currentStep != null)
            {
                if (!DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, currentStep.ApproverRole))
                {
                    TempData["Error"] = $"Bạn không có quyền từ chối phiếu này. Đang chờ chữ ký của: {DuAnCode.Web.Services.RoleHelper.ToVietnamese(currentStep.ApproverRole)}.";
                    return RedirectToAction("Index");
                }

                currentStep.Status = "REJECTED";
                currentStep.ApprovedBy = User.Identity?.Name;
                currentStep.ApprovedAt = DateTime.UtcNow;
            }

            voucher.Status = "REJECTED";
            voucher.IsLocked = true;
            voucher.ApprovedBy = User?.Identity?.Name;
            var rejectNote = string.IsNullOrWhiteSpace(reason) ? "Không có lý do cụ thể" : reason.Trim();
            voucher.Reason = string.IsNullOrWhiteSpace(voucher.Reason) 
                ? $"[TỪ CHỐI DUYỆT: {rejectNote}]" 
                : $"{voucher.Reason} | [TỪ CHỐI DUYỆT: {rejectNote}]";

            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "InventoryVoucher",
                EntityId = voucher.VoucherId,
                Action = "REJECT",
                PerformedBy = User?.Identity?.Name ?? "System",
                Reason = $"Từ chối duyệt phiếu nhập kho. Lý do: {rejectNote}"
            });

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã từ chối duyệt phiếu nhập kho thành công.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> PrintLabels(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();
            var voucher = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id);
            if (voucher == null) return NotFound();

            var config = await _db.SystemConfigs.FirstOrDefaultAsync() ?? new SystemConfig();
            
            ViewBag.Config = config;
            return View(voucher);
        }

        [HttpGet]
        public async Task<IActionResult> GetRejectionDetails(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { success = false, message = "Mã phiếu không hợp lệ." });

            var voucher = await _db.InventoryVouchers.FirstOrDefaultAsync(v => v.VoucherId == id);
            if (voucher == null) return NotFound(new { success = false, message = "Không tìm thấy phiếu." });

            var (originalReason, rejectionReason) = DuAnCode.Web.Services.RejectionHelper.ExtractReason(voucher.Reason);
            var rejectLog = await _db.AuditLogs
                .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryVoucher" && a.EntityId == voucher.VoucherId)
                .OrderByDescending(a => a.PerformedAt)
                .FirstOrDefaultAsync();

            return Json(new
            {
                success = true,
                voucherId = voucher.VoucherId,
                voucherNumber = voucher.VoucherNumber,
                status = voucher.Status,
                createdBy = voucher.CreatedBy ?? "Không xác định",
                createdAt = voucher.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                rejectedBy = rejectLog?.PerformedBy ?? voucher.ApprovedBy ?? "Quản lý kho",
                rejectedAt = (rejectLog?.PerformedAt ?? voucher.Date).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                rejectionReason = rejectionReason,
                originalReason = originalReason
            });
        }
    }
}
