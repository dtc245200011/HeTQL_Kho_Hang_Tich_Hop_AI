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
        private readonly DuAnCode.Web.Services.IApprovalWorkflowService _workflow;

        public StockTransferController(ApplicationDbContext db, IInventoryRepository repo, DuAnCode.Web.Services.IApprovalWorkflowService? workflow = null)
        {
            _db = db;
            _repo = repo;
            _workflow = workflow ?? new DuAnCode.Web.Services.ApprovalWorkflowService(db);
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
                .Where(v => v.Type == "TRANSFER")
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(v => v.VoucherNumber.Contains(s) || (v.CreatedBy != null && v.CreatedBy.Contains(s)) || (v.ReferenceDocument != null && v.ReferenceDocument.Contains(s)) || (v.Reason != null && v.Reason.Contains(s)));
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
            var vm = new Models.ViewModels.VoucherPageViewModel();
            vm.Skus = await GetSkuOptionsAsync();
            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            vm.Recent = new List<InventoryVoucher>();
            
            if (!string.IsNullOrWhiteSpace(id))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id && v.Type == "TRANSFER");
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

                    vm.Form = new Models.ViewModels.VoucherFormViewModel
                    {
                        VoucherId = existing.VoucherId,
                        VoucherNumber = existing.VoucherNumber,
                        Date = existing.Date,
                        Counterparty = existing.Counterparty ?? string.Empty,
                        Address = existing.Address ?? string.Empty,
                        Reason = string.IsNullOrWhiteSpace(originalReason) ? existing.Reason : originalReason,
                        ReferenceDocument = existing.ReferenceDocument ?? string.Empty,
                        WarehouseId = existing.WarehouseId ?? string.Empty,
                        ToWarehouseId = existing.ToWarehouseId,
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
                            var prodName = !string.IsNullOrWhiteSpace(l.ProductName) ? l.ProductName : (skuOpt?.ProductName ?? l.SkuId);
                            var unit = !string.IsNullOrWhiteSpace(l.Unit) ? l.Unit : (skuOpt?.Unit ?? "Cái");

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
                                UnitPrice = l.UnitPrice
                            };
                        }).ToList()
                    };
                }
                else
                {
                    var mainWh = vm.Warehouses.FirstOrDefault(w => w.WarehouseType == "MAIN" && w.IsActive)?.WarehouseId 
                              ?? vm.Warehouses.FirstOrDefault(w => w.WarehouseType != "DAMAGED" && w.IsActive)?.WarehouseId 
                              ?? vm.Warehouses.FirstOrDefault()?.WarehouseId ?? "";
                    var destWh = vm.Warehouses.FirstOrDefault(w => w.WarehouseId != mainWh && w.IsActive)?.WarehouseId ?? "";

                    vm.Form = new Models.ViewModels.VoucherFormViewModel 
                    { 
                        WarehouseId = mainWh,
                        ToWarehouseId = destWh,
                        Lines = new List<Models.ViewModels.VoucherLineInput>() 
                    };
                }
            }
            else
            {
                var mainWh = vm.Warehouses.FirstOrDefault(w => w.WarehouseType == "MAIN" && w.IsActive)?.WarehouseId 
                          ?? vm.Warehouses.FirstOrDefault(w => w.WarehouseType != "DAMAGED" && w.IsActive)?.WarehouseId 
                          ?? vm.Warehouses.FirstOrDefault()?.WarehouseId ?? "";
                var destWh = vm.Warehouses.FirstOrDefault(w => w.WarehouseId != mainWh && w.IsActive)?.WarehouseId ?? "";

                vm.Form = new Models.ViewModels.VoucherFormViewModel 
                { 
                    WarehouseId = mainWh,
                    ToWarehouseId = destWh,
                    Lines = new List<Models.ViewModels.VoucherLineInput>() 
                };
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

            if (form == null || string.IsNullOrWhiteSpace(form.WarehouseId) || string.IsNullOrWhiteSpace(form.ToWarehouseId) || form.Lines == null || !form.Lines.Any())
            {
                TempData["Error"] = "Dữ liệu phiếu không hợp lệ. Vui lòng chọn cả Kho Nguồn, Kho Đích và ít nhất một dòng hàng hóa.";
                return RedirectToAction("Index");
            }

            if (form.WarehouseId == form.ToWarehouseId)
            {
                var vm = new Models.ViewModels.VoucherPageViewModel();
                vm.Skus = await GetSkuOptionsAsync();
                vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
                vm.Recent = new List<InventoryVoucher>();
                vm.Form = form;
                TempData["Error"] = "Lỗi: Kho Nguồn và Kho Đích không được trùng nhau.";
                return View("Create", vm);
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
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
                    existing.ToWarehouseId = form.ToWarehouseId;
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
                    TempData["Message"] = "Cập nhật phiếu chuyển kho (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
                else
                {
                    // CREATE DRAFT FLOW
                    var voucher = new InventoryVoucher
                    {
                        VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", StringComparison.OrdinalIgnoreCase)) ? ("CK-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.VoucherNumber,
                        Date = form.Date ?? DateTime.UtcNow,
                        Type = "TRANSFER",
                        Status = "DRAFT",
                        Counterparty = form.Counterparty,
                        Address = form.Address,
                        Reason = form.Reason,
                        ReferenceDocument = form.ReferenceDocument ?? string.Empty,
                        WarehouseId = form.WarehouseId,
                        ToWarehouseId = form.ToWarehouseId,
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
                    TempData["Message"] = "Tạo phiếu chuyển kho (NHÁP) thành công.";
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
                voucher.ReferenceDocument = "LCK-" + voucher.VoucherNumber;
            }

            if (string.IsNullOrWhiteSpace(voucher.ToWarehouseId))
            {
                var dest = await _db.Warehouses.FirstOrDefaultAsync(w => w.WarehouseId != voucher.WarehouseId);
                if (dest != null)
                {
                    voucher.ToWarehouseId = dest.WarehouseId;
                }
                else
                {
                    TempData["Error"] = "Lỗi: Phiếu chuyển kho phải có Kho Đích.";
                    return RedirectToAction("Index");
                }
            }

            if (voucher.WarehouseId == voucher.ToWarehouseId)
            {
                TempData["Error"] = "Lỗi: Kho Nguồn và Kho Đích không được trùng nhau.";
                return RedirectToAction("Index");
            }

            // --- WORKFLOW LOGIC ---
            var config = await _db.SystemConfigs.FirstOrDefaultAsync();
            if (config != null && config.TransferRequireApproval)
            {
                var steps = await _db.ApprovalSteps.Where(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
                if (!steps.Any() && voucher.Status == "DRAFT" && config.EnableMultiLevelApproval && !DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, "WarehouseKeeper") && !DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, "Thủ kho"))
                {
                    await _workflow.EnsureApprovalStepsForVoucherAsync(voucher);
                    voucher.Status = "PENDING";
                    await _db.SaveChangesAsync();
                    steps = await _db.ApprovalSteps.Where(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId).OrderBy(s => s.Level).ToListAsync();
                }

                var currentStep = steps.FirstOrDefault(s => s.Status == "PENDING");
                if (currentStep != null)
                {
                    if (!DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, currentStep.ApproverRole) && !DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, "Admin") && !DuAnCode.Web.Services.RoleHelper.IsUserInRole(User, "Quản trị viên"))
                    {
                        TempData["Error"] = $"Bạn không có quyền duyệt phiếu này. Đang chờ chữ ký của: {DuAnCode.Web.Services.RoleHelper.ToVietnamese(currentStep.ApproverRole)}.";
                        return RedirectToAction("Index");
                    }

                    currentStep.Status = "APPROVED";
                    currentStep.ApprovedBy = User.Identity?.Name;
                    currentStep.ApprovedAt = DateTime.UtcNow;

                    _db.AuditLogs.Add(new AuditLog
                    {
                        EntityType = "InventoryVoucher",
                        EntityId = voucher.VoucherId,
                        Action = $"APPROVE_LEVEL_{currentStep.Level}",
                        PerformedBy = User.Identity?.Name ?? "System",
                        Reason = $"Duyệt phiếu chuyển kho cấp {currentStep.Level} ({DuAnCode.Web.Services.RoleHelper.ToVietnamese(currentStep.ApproverRole)})"
                    });
                    await _db.SaveChangesAsync();

                    var nextStep = steps.FirstOrDefault(s => s.Status == "PENDING");
                    if (nextStep != null)
                    {
                        TempData["Message"] = $"Đã ký duyệt cấp {currentStep.Level} thành công. Chờ {DuAnCode.Web.Services.RoleHelper.ToVietnamese(nextStep.ApproverRole)} duyệt tiếp.";
                        return RedirectToAction("Index");
                    }
                }
            }
            // --- END WORKFLOW LOGIC ---

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var comboIds = voucher.Lines.Select(l => l.SkuId).Distinct().ToList();
                var combos = await _db.ComboProducts.Include(c => c.Components).Where(c => comboIds.Contains(c.ComboId)).ToListAsync();

                // Aggregate items to transfer (exploding combos into BOM components)
                var transferTotals = new Dictionary<(string SkuId, string BatchNumber, DateTime? ExpiryDate), int>();
                var displayNames = new Dictionary<string, string>();

                foreach (var line in voucher.Lines)
                {
                    if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;

                    var combo = combos.FirstOrDefault(c => c.ComboId == line.SkuId);
                    if (combo != null && combo.Components != null && combo.Components.Any())
                    {
                        foreach (var bom in combo.Components)
                        {
                            var key = (bom.SkuId, line.BatchNumber ?? string.Empty, line.ExpiryDate);
                            var needed = line.Quantity * Math.Max(1, bom.QuantityPerSet);
                            transferTotals[key] = transferTotals.GetValueOrDefault(key) + needed;
                            displayNames[bom.SkuId] = $"{bom.SkuId} (thuộc combo {combo.ComboName})";
                        }
                    }
                    else
                    {
                        var key = (line.SkuId, line.BatchNumber ?? string.Empty, line.ExpiryDate);
                        transferTotals[key] = transferTotals.GetValueOrDefault(key) + line.Quantity;
                        if (!displayNames.ContainsKey(line.SkuId))
                            displayNames[line.SkuId] = line.ProductName ?? line.SkuId;
                    }
                }

                var skusToLock = transferTotals.Keys.Select(k => k.SkuId).Distinct().OrderBy(s => s).ToList();

                // DEADLOCK PREVENTION: Lock warehouses in alphabetical order
                var firstWh = string.Compare(voucher.WarehouseId, voucher.ToWarehouseId, StringComparison.Ordinal) < 0 ? voucher.WarehouseId : voucher.ToWarehouseId;
                var secondWh = firstWh == voucher.WarehouseId ? voucher.ToWarehouseId : voucher.WarehouseId;

                var lockedFirstLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, firstWh, "GOOD");
                var lockedSecondLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, secondWh, "GOOD");

                var lockedSourceLedgers = voucher.WarehouseId == firstWh ? lockedFirstLedgers : lockedSecondLedgers;
                var lockedDestLedgers = voucher.ToWarehouseId == firstWh ? lockedFirstLedgers : lockedSecondLedgers;

                // Check Overstock at Source
                foreach (var kvp in transferTotals)
                {
                    var (skuId, batchNumber, _) = kvp.Key;
                    var totalNeeded = kvp.Value;

                    var sourceLedger = lockedSourceLedgers.FirstOrDefault(l => l.SkuId == skuId && l.BatchNumber == batchNumber);
                    var available = sourceLedger?.Quantity ?? 0;
                    if (sourceLedger == null || available < totalNeeded)
                    {
                        var name = displayNames.GetValueOrDefault(skuId, skuId);
                        TempData["Error"] = $"BR-05: Không đủ tồn kho cho sản phẩm/cấu kiện {name} (Lô: {batchNumber}) tại Kho Nguồn. Tồn hiện tại: {available}, Yêu cầu chuyển: {totalNeeded}";
                        await transaction.RollbackAsync();
                        return RedirectToAction("Index");
                    }
                }

                // Apply transfer
                foreach (var kvp in transferTotals)
                {
                    var (skuId, batchNumber, expiryDate) = kvp.Key;
                    var totalNeeded = kvp.Value;

                    // 1. Giảm Tồn kho nguồn
                    var sourceLedger = lockedSourceLedgers.First(l => l.SkuId == skuId && l.BatchNumber == batchNumber);
                    sourceLedger.Quantity -= totalNeeded;
                    await _repo.UpdateLedgerAsync(sourceLedger);

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = skuId,
                        WarehouseId = voucher.WarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = -totalNeeded,
                        MovementType = "TRANSFER_OUT",
                        ReferenceType = "VOUCHER",
                        ReferenceId = voucher.VoucherId,
                        PerformedBy = User?.Identity?.Name,
                        CreatedAt = DateTime.UtcNow
                    });

                    // 2. Tăng Tồn kho đích
                    var destLedger = lockedDestLedgers.FirstOrDefault(l => l.SkuId == skuId && l.BatchNumber == batchNumber);
                    if (destLedger == null)
                    {
                        destLedger = new StockLedger
                        {
                            SkuId = skuId,
                            WarehouseId = voucher.ToWarehouseId,
                            Status = "GOOD",
                            Quantity = totalNeeded,
                            BatchNumber = batchNumber,
                            ExpiryDate = expiryDate
                        };
                        await _repo.AddLedgerAsync(destLedger);
                        lockedDestLedgers.Add(destLedger);
                    }
                    else
                    {
                        destLedger.Quantity += totalNeeded;
                        await _repo.UpdateLedgerAsync(destLedger);
                    }

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = skuId,
                        WarehouseId = voucher.ToWarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = totalNeeded,
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

                TempData["Message"] = "Duyệt phiếu chuyển kho thành công! Tồn kho đã được điều chuyển.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[StockTransferController Approve Error]: {ex}");
                TempData["Error"] = "Lỗi khi duyệt phiếu chuyển kho.";
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
                Reason = $"Từ chối duyệt phiếu chuyển kho. Lý do: {rejectNote}"
            });

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã từ chối duyệt phiếu chuyển kho thành công.";
            return RedirectToAction("Index");
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
