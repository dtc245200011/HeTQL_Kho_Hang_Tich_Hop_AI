using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Controllers
{
    public class InventoryAuditController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IInventoryRepository _repo;

        public InventoryAuditController(ApplicationDbContext db, IInventoryRepository repo)
        {
            _db = db;
            _repo = repo;
        }

        private async Task<List<DuAnCode.Web.Models.ViewModels.SkuDto>> GetSkuOptionsAsync()
        {
            var skus = await _db.SkuVariants.AsNoTracking().ToListAsync();
            var pmMap = await _db.ProductModels.AsNoTracking().ToListAsync();
            var comboProducts = await _db.ComboProducts.AsNoTracking().ToListAsync();

            var list = skus.Select(s =>
            {
                var pm = pmMap.FirstOrDefault(p => p.ProductModelId == s.ProductModelId);
                return new DuAnCode.Web.Models.ViewModels.SkuDto
                {
                    SkuId = s.SkuId,
                    ProductName = pm?.ProductName ?? string.Empty,
                    Unit = pm?.Unit ?? "Cái"
                };
            }).ToList();

            list.AddRange(comboProducts.Select(c => new DuAnCode.Web.Models.ViewModels.SkuDto
            {
                SkuId = c.ComboId,
                ProductName = "[COMBO] " + c.ComboName,
                Unit = "Bộ"
            }));

            return list.OrderBy(x => x.SkuId).ToList();
        }

        public async Task<IActionResult> Index(string? search = null, string? status = null, DateTime? fromDate = null, DateTime? toDate = null)
        {
            var query = _db.InventoryAudits.Include(v => v.Lines).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var s = search.Trim();
                query = query.Where(v => v.AuditNumber.Contains(s) || (v.CreatedBy != null && v.CreatedBy.Contains(s)) || (v.WarehouseId != null && v.WarehouseId.Contains(s)) || (v.Reason != null && v.Reason.Contains(s)));
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

            var audits = await query.OrderByDescending(x => x.Date).ToListAsync();

            var rejectedIds = audits.Where(v => v.Status == "REJECTED").Select(v => v.AuditId).ToList();
            if (rejectedIds.Any())
            {
                var logs = await _db.AuditLogs
                    .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryAudit" && rejectedIds.Contains(a.EntityId))
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

            return View(audits);
        }

        [HttpGet]
        public async Task<IActionResult> Create(string? id = null)
        {
            ViewBag.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
            ViewBag.Skus = await GetSkuOptionsAsync();

            if (!string.IsNullOrWhiteSpace(id))
            {
                var existing = await _db.InventoryAudits
                    .Include(a => a.Lines)
                    .FirstOrDefaultAsync(a => a.AuditId == id);

                if (existing != null)
                {
                    if (existing.Status == "REJECTED")
                    {
                        var rejectLog = await _db.AuditLogs
                            .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryAudit" && a.EntityId == existing.AuditId)
                            .OrderByDescending(a => a.PerformedAt)
                            .FirstOrDefaultAsync();
                        ViewBag.RejectLog = rejectLog;
                    }
                    return View(existing);
                }
            }

            return View(new InventoryAudit());
        }

        [HttpPost]
        public async Task<IActionResult> Create(InventoryAudit form)
        {
            if (string.IsNullOrWhiteSpace(form.WarehouseId))
            {
                TempData["Error"] = "Vui lòng chọn Kho.";
                ViewBag.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
                ViewBag.Skus = await GetSkuOptionsAsync();
                return View("Create", form);
            }

            var cleanLines = (form.Lines ?? new List<InventoryAuditLine>())
                .Where(l => !string.IsNullOrWhiteSpace(l.SkuId))
                .Select(l => {
                    l.BatchNumber = l.BatchNumber ?? string.Empty;
                    l.StockStatus = string.IsNullOrWhiteSpace(l.StockStatus) ? "GOOD" : l.StockStatus.ToUpper();
                    l.AuditId = form.AuditId;
                    return l;
                }).ToList();

            try 
            {
                var existing = await _db.InventoryAudits.Include(a => a.Lines).FirstOrDefaultAsync(a => a.AuditId == form.AuditId);
                if (existing != null)
                {
                    if (existing.Status == "APPROVED" || existing.Status == "REJECTED")
                    {
                        TempData["Error"] = "Phiếu kiểm kê đã khóa, không thể chỉnh sửa.";
                        return RedirectToAction("Index");
                    }

                    existing.WarehouseId = form.WarehouseId;
                    existing.Date = form.Date;
                    existing.Reason = form.Reason;

                    _db.InventoryAuditLines.RemoveRange(existing.Lines);
                    existing.Lines.Clear();
                    foreach (var l in cleanLines)
                    {
                        existing.Lines.Add(new InventoryAuditLine
                        {
                            AuditId = existing.AuditId,
                            SkuId = l.SkuId,
                            BatchNumber = l.BatchNumber ?? string.Empty,
                            StockStatus = string.IsNullOrWhiteSpace(l.StockStatus) ? "GOOD" : l.StockStatus.ToUpper(),
                            SystemQuantity = l.SystemQuantity,
                            ActualQuantity = l.ActualQuantity,
                            Notes = l.Notes
                        });
                    }

                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Cập nhật phiếu kiểm kê thành công";
                    return RedirectToAction("Index");
                }
                else
                {
                    form.AuditNumber = (string.IsNullOrWhiteSpace(form.AuditNumber) || form.AuditNumber == "AUTO") ? ("KK-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.AuditNumber;
                    form.CreatedBy = !string.IsNullOrWhiteSpace(form.CreatedBy) ? form.CreatedBy : (User?.Identity?.Name ?? "admin");
                    if (form.Date == default) form.Date = DateTime.UtcNow;
                    form.Lines = cleanLines;

                    _db.InventoryAudits.Add(form);
                    await _db.SaveChangesAsync();
                    TempData["Success"] = "Tạo phiếu kiểm kê thành công";
                    return RedirectToAction("Index");
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi hệ thống: " + (ex.InnerException?.Message ?? ex.Message);
                ViewBag.Warehouses = await _db.Warehouses.AsNoTracking().ToListAsync();
                ViewBag.Skus = await GetSkuOptionsAsync();
                return View("Create", form);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin,Director,WarehouseManager,Quản lý kho,Quản trị viên,Giám đốc")]
        public async Task<IActionResult> Approve(string id)
        {
            var audit = await _db.InventoryAudits.Include(a => a.Lines).FirstOrDefaultAsync(a => a.AuditId == id);
            if (audit == null) return NotFound();
            if (audit.Status != "DRAFT" && audit.Status != "PENDING") { TempData["Error"] = "Phiếu đã được xử lý trước đó."; return RedirectToAction("Index"); }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var skusToLock = audit.Lines.Select(l => l.SkuId).Distinct().ToList();
                var distinctStatuses = audit.Lines
                    .Select(l => string.IsNullOrWhiteSpace(l.StockStatus) ? "GOOD" : l.StockStatus.ToUpper())
                    .Distinct()
                    .ToList();

                var lockedLedgers = new List<StockLedger>();
                foreach (var status in distinctStatuses)
                {
                    var locked = await _repo.LockLedgersForUpdateAsync(skusToLock, audit.WarehouseId, status);
                    lockedLedgers.AddRange(locked);
                }
                
                var adjustmentLines = new List<VoucherLine>();
                
                foreach(var line in audit.Lines)
                {
                    var lineStatus = string.IsNullOrWhiteSpace(line.StockStatus) ? "GOOD" : line.StockStatus.ToUpper();
                    var ledger = lockedLedgers.FirstOrDefault(l => l.SkuId == line.SkuId && l.BatchNumber == line.BatchNumber && l.Status == lineStatus);
                    int systemQty = ledger?.Quantity ?? 0;
                    line.SystemQuantity = systemQty;
                    
                    int diff = line.ActualQuantity - systemQty;
                    
                    if (diff != 0)
                    {
                        // BR-06: Chênh lệch tồn -> Cập nhật tồn & sinh phiếu điều chỉnh (Adjustment)
                        if (ledger == null)
                        {
                            ledger = new StockLedger { SkuId = line.SkuId, WarehouseId = audit.WarehouseId, Status = lineStatus, Quantity = line.ActualQuantity, BatchNumber = line.BatchNumber };
                            await _repo.AddLedgerAsync(ledger);
                            lockedLedgers.Add(ledger);
                        }
                        else
                        {
                            ledger.Quantity = line.ActualQuantity;
                            await _repo.UpdateLedgerAsync(ledger);
                        }
                        
                        await _repo.AddMovementAsync(new StockMovement
                        {
                            SkuId = line.SkuId, WarehouseId = audit.WarehouseId, StockStatus = lineStatus, QuantityDelta = diff, MovementType = "AUDIT_ADJUSTMENT", ReferenceType = "AUDIT", ReferenceId = audit.AuditId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow
                        });
                        
                        adjustmentLines.Add(new VoucherLine {
                            SkuId = line.SkuId, SkuCode = line.SkuId, Quantity = Math.Abs(diff), BatchNumber = line.BatchNumber
                        });
                    }
                }
                
                audit.Status = "APPROVED";
                audit.ApprovedBy = User?.Identity?.Name;
                
                _db.AuditLogs.Add(new AuditLog { EntityType = "InventoryAudit", EntityId = audit.AuditId, Action = "APPROVE", PerformedBy = User?.Identity?.Name ?? "System", Reason = "Duyệt phiếu kiểm kê kho" });
                
                if (adjustmentLines.Any())
                {
                    var adjVoucher = new InventoryVoucher
                    {
                        VoucherNumber = "ADJ-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss"), Date = DateTime.UtcNow, Type = "ADJUSTMENT", Status = "APPROVED", WarehouseId = audit.WarehouseId, Reason = "Điều chỉnh sau kiểm kê " + audit.AuditNumber, ReferenceDocument = audit.AuditNumber, CreatedBy = User?.Identity?.Name, ApprovedBy = User?.Identity?.Name, IsLocked = true, Lines = adjustmentLines
                    };
                    _db.InventoryVouchers.Add(adjVoucher);
                }
                
                await _db.SaveChangesAsync();
                await transaction.CommitAsync();
                
                TempData["Message"] = "Duyệt phiếu kiểm kê thành công!";
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Lỗi khi duyệt phiếu kiểm kê: " + ex.Message;
            }
            
            return RedirectToAction("Index");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(string id, string? reason)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest();

            var audit = await _db.InventoryAudits.FirstOrDefaultAsync(a => a.AuditId == id);
            if (audit == null) return NotFound();
            if (audit.Status != "DRAFT") { TempData["Error"] = "Chỉ có thể từ chối phiếu kiểm kê đang ở trạng thái Nháp."; return RedirectToAction("Index"); }

            audit.Status = "REJECTED";
            audit.ApprovedBy = User?.Identity?.Name;
            var rejectNote = string.IsNullOrWhiteSpace(reason) ? "Không có lý do cụ thể" : reason.Trim();
            audit.Reason = string.IsNullOrWhiteSpace(audit.Reason) 
                ? $"[TỪ CHỐI DUYỆT: {rejectNote}]" 
                : $"{audit.Reason} | [TỪ CHỐI DUYỆT: {rejectNote}]";

            _db.AuditLogs.Add(new AuditLog 
            { 
                EntityType = "InventoryAudit", 
                EntityId = audit.AuditId, 
                Action = "REJECT", 
                PerformedBy = User?.Identity?.Name ?? "System", 
                Reason = $"Từ chối duyệt phiếu kiểm kê kho. Lý do: {rejectNote}" 
            });

            await _db.SaveChangesAsync();
            TempData["Message"] = "Đã từ chối duyệt phiếu kiểm kê kho thành công.";
            return RedirectToAction("Index");
        }

        [HttpGet]
        public async Task<IActionResult> GetRejectionDetails(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return BadRequest(new { success = false, message = "Mã phiếu không hợp lệ." });

            var audit = await _db.InventoryAudits.FirstOrDefaultAsync(v => v.AuditId == id);
            if (audit == null) return NotFound(new { success = false, message = "Không tìm thấy phiếu kiểm kê." });

            var (originalReason, rejectionReason) = DuAnCode.Web.Services.RejectionHelper.ExtractReason(audit.Reason);
            var rejectLog = await _db.AuditLogs
                .Where(a => a.Action == "REJECT" && a.EntityType == "InventoryAudit" && a.EntityId == audit.AuditId)
                .OrderByDescending(a => a.PerformedAt)
                .FirstOrDefaultAsync();

            return Json(new
            {
                success = true,
                voucherId = audit.AuditId,
                voucherNumber = audit.AuditNumber,
                status = audit.Status,
                createdBy = audit.CreatedBy ?? "Không xác định",
                createdAt = audit.Date.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                rejectedBy = rejectLog?.PerformedBy ?? audit.ApprovedBy ?? "Quản lý kho",
                rejectedAt = (rejectLog?.PerformedAt ?? audit.Date).ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                rejectionReason = rejectionReason,
                originalReason = originalReason
            });
        }
    }
}
