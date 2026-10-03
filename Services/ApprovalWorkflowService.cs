using DuAnCode.Web.Models;
using DuAnCode.Web.Data;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Services
{
    public interface IApprovalWorkflowService
    {
        Task EnsureApprovalStepsForPurchaseAsync(PurchaseOrder po);
        Task EnsureApprovalStepsForSalesAsync(SalesOrder so);
        Task EnsureApprovalStepsForVoucherAsync(InventoryVoucher voucher);
    }

    public class ApprovalWorkflowService : IApprovalWorkflowService
    {
        private readonly ApplicationDbContext _db;
        public ApprovalWorkflowService(ApplicationDbContext db) { _db = db; }

        public async Task EnsureApprovalStepsForPurchaseAsync(PurchaseOrder po)
        {
            // Purchase Orders require 2 levels: L1 Accountant, L2 WarehouseManager
            var steps = new List<ApprovalStep>
            {
                new ApprovalStep { EntityType = "PO", EntityId = po.PoId, Level = 1, ApproverRole = "Accountant", Status = "PENDING" },
                new ApprovalStep { EntityType = "PO", EntityId = po.PoId, Level = 2, ApproverRole = "WarehouseManager", Status = "PENDING" }
            };
            _db.ApprovalSteps.AddRange(steps);
            await _db.SaveChangesAsync();
        }

        public async Task EnsureApprovalStepsForSalesAsync(SalesOrder so)
        {
            // SalesOrder approval levels depend on TotalAmount
            var steps = new List<ApprovalStep>();
            steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 1, ApproverRole = "Accountant", Status = "PENDING" });
            steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 2, ApproverRole = "WarehouseManager", Status = "PENDING" });

            if (so.TotalAmount > 100000000M)
            {
                steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 3, ApproverRole = "Director", Status = "PENDING" });
            }

            _db.ApprovalSteps.AddRange(steps);
            await _db.SaveChangesAsync();
        }

        public async Task EnsureApprovalStepsForVoucherAsync(InventoryVoucher voucher)
        {
            var steps = new List<ApprovalStep>();
            
            // Cấp 1: Kế toán
            steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = "Accountant", Status = "PENDING" });
            
            // Cấp 2: Quản lý kho
            steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = "WarehouseManager", Status = "PENDING" });

            // Cấp 3: Giám đốc (chỉ áp dụng nếu Phiếu Xuất và > 100 triệu VNĐ)
            if (voucher.Type == "OUTBOUND" && voucher.TotalAmount > 100000000M)
            {
                steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 3, ApproverRole = "Director", Status = "PENDING" });
            }

            _db.ApprovalSteps.AddRange(steps);
            await _db.SaveChangesAsync();
        }
    }
}
