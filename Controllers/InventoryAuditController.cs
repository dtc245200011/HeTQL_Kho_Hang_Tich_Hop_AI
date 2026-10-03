using DuAnCode.Web.Data;
using DuAnCode.Web.Models;
using DuAnCode.Web.Repositories;
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

        public async Task<IActionResult> Index()
        {
            var audits = await _db.InventoryAudits.OrderByDescending(x => x.Date).ToListAsync();
            return View(audits);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            ViewBag.Warehouses = await _db.Warehouses.ToListAsync();
            return View(new InventoryAudit());
        }

        [HttpPost]
        public async Task<IActionResult> Create(InventoryAudit form)
        {
            if (string.IsNullOrWhiteSpace(form.WarehouseId))
            {
                TempData["Error"] = "Vui lòng chọn Kho.";
                return RedirectToAction("Create");
            }

            form.AuditNumber = (string.IsNullOrWhiteSpace(form.AuditNumber) || form.AuditNumber == "AUTO") ? ("KK-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss")) : form.AuditNumber;
            form.CreatedBy = User?.Identity?.Name;
            form.Date = DateTime.UtcNow;
            
            // Remove empty lines
            form.Lines = form.Lines.Where(l => !string.IsNullOrWhiteSpace(l.SkuId)).Select(l => {
                l.BatchNumber = l.BatchNumber ?? string.Empty;
                l.AuditId = form.AuditId;
                return l;
            }).ToList();


            
            try 
            {
                _db.InventoryAudits.Add(form);
                await _db.SaveChangesAsync();
                TempData["Success"] = "Tạo phiếu kiểm kê thành công";
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Lỗi hệ thống: " + (ex.InnerException?.Message ?? ex.Message);
                ViewBag.Warehouses = await _db.Warehouses.ToListAsync();
                return View("Create", form);
            }
        }

        [HttpPost]
        public async Task<IActionResult> Approve(string id)
        {
            var audit = await _db.InventoryAudits.Include(a => a.Lines).FirstOrDefaultAsync(a => a.AuditId == id);
            if (audit == null) return NotFound();
            if (audit.Status != "DRAFT") { TempData["Error"] = "Phiếu đã duyệt."; return RedirectToAction("Index"); }

            await using var transaction = await _db.Database.BeginTransactionAsync();
            try
            {
                var skusToLock = audit.Lines.Select(l => l.SkuId).Distinct().ToList();
                var lockedLedgers = await _repo.LockLedgersForUpdateAsync(skusToLock, audit.WarehouseId, "GOOD");
                
                var adjustmentLines = new List<VoucherLine>();
                
                foreach(var line in audit.Lines)
                {
                    var ledger = lockedLedgers.FirstOrDefault(l => l.SkuId == line.SkuId && l.BatchNumber == line.BatchNumber);
                    int systemQty = ledger?.Quantity ?? 0;
                    line.SystemQuantity = systemQty;
                    
                    int diff = line.ActualQuantity - systemQty;
                    
                    if (diff != 0)
                    {
                        // BR-06: Chênh lệch tồn -> Cập nhật tồn & sinh phiếu điều chỉnh (Adjustment)
                        if (ledger == null)
                        {
                            ledger = new StockLedger { SkuId = line.SkuId, WarehouseId = audit.WarehouseId, Status = "GOOD", Quantity = 0, BatchNumber = line.BatchNumber };
                            await _repo.AddLedgerAsync(ledger);
                            lockedLedgers.Add(ledger);
                        }
                        
                        ledger.Quantity = line.ActualQuantity;
                        await _repo.UpdateLedgerAsync(ledger);
                        
                        await _repo.AddMovementAsync(new StockMovement
                        {
                            SkuId = line.SkuId, WarehouseId = audit.WarehouseId, StockStatus = "GOOD", QuantityDelta = diff, MovementType = "AUDIT_ADJUSTMENT", ReferenceType = "AUDIT", ReferenceId = audit.AuditId, PerformedBy = User?.Identity?.Name, CreatedAt = DateTime.UtcNow
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
    }
}
