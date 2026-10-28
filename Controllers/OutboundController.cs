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
    public class OutboundController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IInventoryRepository _repo;
        private readonly DuAnCode.Web.Services.IApprovalWorkflowService _workflow;

        public OutboundController(ApplicationDbContext db, IInventoryRepository repo, DuAnCode.Web.Services.IApprovalWorkflowService workflow)
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
                .Where(v => v.Type == "OUTBOUND")
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
                if (config.StopOutbound)
                {
                    TempData["Error"] = "Chức năng tạo Phiếu Xuất Kho đã bị tạm dừng bởi Quản trị viên.";
                    return RedirectToAction("Index");
                }
            }

            var vm = new Models.ViewModels.VoucherPageViewModel();
            vm.Skus = await GetSkuOptionsAsync();
            vm.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            vm.Recent = new List<InventoryVoucher>();
            
            if (!string.IsNullOrWhiteSpace(id))
            {
                var existing = await _db.InventoryVouchers.Include(v => v.Lines).FirstOrDefaultAsync(v => v.VoucherId == id && v.Type == "OUTBOUND");
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
                        Status = existing.Status,
                        IsLocked = existing.IsLocked,
                        CreatedBy = existing.CreatedBy,
                        ApprovedBy = rejectedBy,
                        RejectedAt = rejectedAt,
                        RejectionReason = rejectionReason,
                        OriginalReason = originalReason,
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
            var config = await _db.SystemConfigs.FirstOrDefaultAsync();
            if (config != null && (config.MaintenanceMode || config.StopOutbound))
            {
                TempData["Error"] = "Chức năng tạo Phiếu Xuất Kho đã bị tạm dừng bởi Quản trị viên.";
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
                    TempData["Message"] = "Cập nhật phiếu xuất (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
                else
                {
                    // CREATE DRAFT FLOW
                    var prefix = config?.OutboundPrefix ?? "PX-";
                    var voucher = new InventoryVoucher
                    {
                        VoucherNumber = (string.IsNullOrWhiteSpace(form.VoucherNumber) || string.Equals(form.VoucherNumber, "AUTO", StringComparison.OrdinalIgnoreCase)) ? (prefix + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.VoucherNumber,
                        Date = form.Date ?? DateTime.UtcNow,
                        Type = "OUTBOUND",
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

                    // Kiểm tra cấu hình Tự động duyệt phiếu xuất giá trị nhỏ (AutoApproveLowValueOutbound)
                    if (config != null && config.AutoApproveLowValueOutbound && config.AutoApproveThreshold > 0 && voucher.TotalAmount <= config.AutoApproveThreshold)
                    {
                        var (approved, errMsg) = await ExecuteVoucherApprovalDeductionAsync(voucher, "Tự động duyệt (Hệ thống)", $"Tự động phê duyệt Phiếu Xuất giá trị nhỏ ({voucher.TotalAmount:N0} đ <= Ngưỡng {config.AutoApproveThreshold:N0} đ)");
                        if (approved)
                        {
                            await transaction.CommitAsync();
                            TempData["Message"] = $"Tạo và TỰ ĐỘNG DUYỆT phiếu xuất thành công! (Giá trị {voucher.TotalAmount:N0} VNĐ <= Ngưỡng tự động duyệt {config.AutoApproveThreshold:N0} VNĐ). Tồn kho đã được trừ.";
                            return RedirectToAction("Index");
                        }
                        else
                        {
                            await transaction.CommitAsync();
                            TempData["Warning"] = $"Phiếu xuất đã tạo thành công ở trạng thái NHÁP. Chưa thể tự động duyệt vì: {errMsg}";
                            return RedirectToAction("Index");
                        }
                    }

                    await transaction.CommitAsync();
                    TempData["Message"] = "Tạo phiếu xuất (NHÁP) thành công.";
                    return RedirectToAction("Index");
                }
            }

            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[OutboundController Error]: {ex}");
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

            // BR-02: Phiếu xuất chỉ được trình duyệt khi có yêu cầu xuất hợp lệ từ bộ phận/đơn vị nhận
            if (string.IsNullOrWhiteSpace(voucher.ReferenceDocument))
            {
                TempData["Error"] = "Lỗi BR-02: Phiếu xuất chỉ được duyệt khi có Yêu cầu xuất hợp lệ (Chứng từ gốc).";
                return RedirectToAction("Index");
            }

            var config = await _db.SystemConfigs.AsNoTracking().FirstOrDefaultAsync();

            // Tự động duyệt nếu bật cấu hình giá trị nhỏ
            if (config != null && config.AutoApproveLowValueOutbound && config.AutoApproveThreshold > 0 && voucher.TotalAmount <= config.AutoApproveThreshold)
            {
                await using var autoTx = await _db.Database.BeginTransactionAsync();
                var (autoApproved, autoErr) = await ExecuteVoucherApprovalDeductionAsync(voucher, "Tự động duyệt (Hệ thống)", $"Tự động phê duyệt Phiếu Xuất giá trị nhỏ ({voucher.TotalAmount:N0} đ <= Ngưỡng {config.AutoApproveThreshold:N0} đ)");
                if (autoApproved)
                {
                    await autoTx.CommitAsync();
                    TempData["Message"] = $"Phiếu xuất giá trị nhỏ ({voucher.TotalAmount:N0} VNĐ <= {config.AutoApproveThreshold:N0} VNĐ) đã được TỰ ĐỘNG DUYỆT thành công!";
                    return RedirectToAction("Index");
                }
                else
                {
                    await autoTx.RollbackAsync();
                    TempData["Error"] = autoErr;
                    return RedirectToAction("Index");
                }
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
                var (success, errMsg) = await ExecuteVoucherApprovalDeductionAsync(voucher, User?.Identity?.Name ?? "Quản lý kho", "Duyệt phiếu xuất kho");
                if (!success)
                {
                    TempData["Error"] = errMsg;
                    await transaction.RollbackAsync();
                    return RedirectToAction("Index");
                }

                await transaction.CommitAsync();
                TempData["Message"] = "Duyệt phiếu xuất kho thành công! Tồn kho đã được trừ.";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[OutboundController Approve Error]: {ex}");
                TempData["Error"] = "Lỗi khi duyệt phiếu.";
            }

            return RedirectToAction("Index");
        }

        private async Task<(bool Success, string? ErrorMessage)> ExecuteVoucherApprovalDeductionAsync(InventoryVoucher voucher, string approverName, string reason)
        {
            var comboIds = voucher.Lines.Select(l => l.SkuId).Distinct().ToList();
            var combos = await _db.ComboProducts.Include(c => c.Components).Where(c => comboIds.Contains(c.ComboId)).ToListAsync();

            // Aggregate required quantities across all lines (including Combo explosion)
            var skuRequirements = new Dictionary<string, (int TotalNeeded, string PreferredBatch, string DisplayName, bool IsCombo)>();

            foreach (var line in voucher.Lines)
            {
                if (string.IsNullOrWhiteSpace(line.SkuId) || line.Quantity <= 0) continue;

                var combo = combos.FirstOrDefault(c => c.ComboId == line.SkuId);
                if (combo != null && combo.Components != null && combo.Components.Any())
                {
                    foreach (var bom in combo.Components)
                    {
                        var needed = line.Quantity * Math.Max(1, bom.QuantityPerSet);
                        if (skuRequirements.TryGetValue(bom.SkuId, out var existing))
                        {
                            skuRequirements[bom.SkuId] = (existing.TotalNeeded + needed, existing.PreferredBatch, existing.DisplayName, true);
                        }
                        else
                        {
                            skuRequirements[bom.SkuId] = (needed, string.Empty, $"{bom.SkuId} (thuộc combo {combo.ComboName})", true);
                        }
                    }
                }
                else
                {
                    var displayName = line.ProductName ?? line.SkuId;
                    var preferredBatch = line.BatchNumber?.Trim() ?? string.Empty;
                    if (skuRequirements.TryGetValue(line.SkuId, out var existing))
                    {
                        var mergedBatch = string.IsNullOrEmpty(existing.PreferredBatch) ? preferredBatch : existing.PreferredBatch;
                        skuRequirements[line.SkuId] = (existing.TotalNeeded + line.Quantity, mergedBatch, displayName, false);
                    }
                    else
                    {
                        skuRequirements[line.SkuId] = (line.Quantity, preferredBatch, displayName, false);
                    }
                }
            }

            var skusToLock = skuRequirements.Keys.OrderBy(s => s).ToList();
            var lockedLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, voucher.WarehouseId, "GOOD");

            // 1. Check Overstock against aggregated totals
            foreach (var (skuId, req) in skuRequirements)
            {
                var available = lockedLedgers.Where(l => l.SkuId == skuId).Sum(l => l.Quantity);
                if (available < req.TotalNeeded)
                {
                    var errCode = req.IsCombo ? "ERR-STOCK-002" : "ERR-STOCK-001";
                    return (false, $"{errCode}: Không đủ tồn kho cho sản phẩm/cấu kiện {req.DisplayName}. Tồn hiện tại: {available}, Tổng yêu cầu xuất: {req.TotalNeeded}");
                }
            }

            // 2. Apply deduction using preferred batch when available, then automatic FIFO across all available batches
            foreach (var (skuId, req) in skuRequirements)
            {
                var remaining = req.TotalNeeded;
                var ledgersForSku = lockedLedgers.Where(l => l.SkuId == skuId).ToList();

                if (!string.IsNullOrWhiteSpace(req.PreferredBatch))
                {
                    var preferredLedger = ledgersForSku.FirstOrDefault(l => l.BatchNumber == req.PreferredBatch && l.Quantity > 0);
                    if (preferredLedger != null)
                    {
                        var take = Math.Min(remaining, preferredLedger.Quantity);
                        preferredLedger.Quantity -= take;
                        remaining -= take;
                        await _repo.UpdateLedgerAsync(preferredLedger);

                        await _repo.AddMovementAsync(new StockMovement
                        {
                            SkuId = skuId,
                            WarehouseId = voucher.WarehouseId,
                            StockStatus = "GOOD",
                            QuantityDelta = -take,
                            MovementType = "OUTBOUND",
                            ReferenceType = "VOUCHER",
                            ReferenceId = voucher.VoucherId,
                            PerformedBy = approverName,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                if (remaining > 0)
                {
                    var fifoLedgers = ledgersForSku
                        .Where(l => l.Quantity > 0)
                        .OrderBy(l => l.ExpiryDate.HasValue ? l.ExpiryDate.Value : DateTime.MaxValue)
                        .ThenBy(l => l.BatchNumber)
                        .ToList();

                    foreach (var l in fifoLedgers)
                    {
                        if (remaining <= 0) break;
                        var take = Math.Min(remaining, l.Quantity);
                        l.Quantity -= take;
                        remaining -= take;
                        await _repo.UpdateLedgerAsync(l);

                        await _repo.AddMovementAsync(new StockMovement
                        {
                            SkuId = skuId,
                            WarehouseId = voucher.WarehouseId,
                            StockStatus = "GOOD",
                            QuantityDelta = -take,
                            MovementType = "OUTBOUND",
                            ReferenceType = "VOUCHER",
                            ReferenceId = voucher.VoucherId,
                            PerformedBy = approverName,
                            CreatedAt = DateTime.UtcNow
                        });
                    }
                }

                if (remaining > 0 && ledgersForSku.Any())
                {
                    var fallback = ledgersForSku.First();
                    fallback.Quantity -= remaining;
                    await _repo.UpdateLedgerAsync(fallback);

                    await _repo.AddMovementAsync(new StockMovement
                    {
                        SkuId = skuId,
                        WarehouseId = voucher.WarehouseId,
                        StockStatus = "GOOD",
                        QuantityDelta = -remaining,
                        MovementType = "OUTBOUND",
                        ReferenceType = "VOUCHER",
                        ReferenceId = voucher.VoucherId,
                        PerformedBy = approverName,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            voucher.Status = "APPROVED";
            voucher.IsLocked = true;
            voucher.ApprovedBy = approverName;
            voucher.Date = DateTime.UtcNow;

            // Mark all pending approval steps as approved if any
            var steps = await _db.ApprovalSteps.Where(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId).ToListAsync();
            foreach (var st in steps.Where(s => s.Status == "PENDING"))
            {
                st.Status = "APPROVED";
                st.ApprovedBy = approverName;
                st.ApprovedAt = DateTime.UtcNow;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                EntityType = "InventoryVoucher",
                EntityId = voucher.VoucherId,
                Action = "APPROVE",
                PerformedBy = approverName,
                Reason = reason
            });

            await _db.SaveChangesAsync();
                        return (true, null);
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
                Reason = $"Từ chối duyệt phiếu xuất kho. Lý do: {rejectNote}"
            });

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã từ chối duyệt phiếu xuất kho thành công.";
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
