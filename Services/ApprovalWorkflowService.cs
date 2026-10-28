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
            if (await _db.ApprovalSteps.AnyAsync(s => s.EntityType == "PO" && s.EntityId == po.PoId)) return;

            var config = await _db.SystemConfigs.FirstOrDefaultAsync() ?? new SystemConfig();
            var role1 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel1) ? config.ApprovalRoleLevel1 : RoleHelper.Accountant;
            var role2 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel2) ? config.ApprovalRoleLevel2 : RoleHelper.WarehouseManager;

            var steps = new List<ApprovalStep>();
            if (config.EnableMultiLevelApproval)
            {
                steps.Add(new ApprovalStep { EntityType = "PO", EntityId = po.PoId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                steps.Add(new ApprovalStep { EntityType = "PO", EntityId = po.PoId, Level = 2, ApproverRole = role2, Status = "PENDING" });
            }
            else
            {
                steps.Add(new ApprovalStep { EntityType = "PO", EntityId = po.PoId, Level = 1, ApproverRole = role2, Status = "PENDING" });
            }

            _db.ApprovalSteps.AddRange(steps);
            await _db.SaveChangesAsync();
        }

        public async Task EnsureApprovalStepsForSalesAsync(SalesOrder so)
        {
            if (await _db.ApprovalSteps.AnyAsync(s => s.EntityType == "SO" && s.EntityId == so.SoId)) return;

            var config = await _db.SystemConfigs.FirstOrDefaultAsync() ?? new SystemConfig();
            var role1 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel1) ? config.ApprovalRoleLevel1 : RoleHelper.Accountant;
            var role2 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel2) ? config.ApprovalRoleLevel2 : RoleHelper.WarehouseManager;
            var role3 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel3) ? config.ApprovalRoleLevel3 : RoleHelper.Director;
            var threshold = config.OutboundDirectorApprovalThreshold > 0 ? config.OutboundDirectorApprovalThreshold : 100000000M;

            var steps = new List<ApprovalStep>();
            if (config.EnableMultiLevelApproval)
            {
                steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 2, ApproverRole = role2, Status = "PENDING" });

                if (so.TotalAmount > threshold)
                {
                    steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 3, ApproverRole = role3, Status = "PENDING" });
                }
            }
            else
            {
                steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 1, ApproverRole = role2, Status = "PENDING" });
                if (so.TotalAmount > threshold)
                {
                    steps.Add(new ApprovalStep { EntityType = "SO", EntityId = so.SoId, Level = 2, ApproverRole = role3, Status = "PENDING" });
                }
            }

            _db.ApprovalSteps.AddRange(steps);
            await _db.SaveChangesAsync();
        }

        public async Task EnsureApprovalStepsForVoucherAsync(InventoryVoucher voucher)
        {
            if (await _db.ApprovalSteps.AnyAsync(s => s.EntityType == "VOUCHER" && s.EntityId == voucher.VoucherId)) return;

            var config = await _db.SystemConfigs.FirstOrDefaultAsync() ?? new SystemConfig();
            var role1 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel1) ? config.ApprovalRoleLevel1 : RoleHelper.Accountant;
            var role2 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel2) ? config.ApprovalRoleLevel2 : RoleHelper.WarehouseManager;
            var role3 = !string.IsNullOrWhiteSpace(config.ApprovalRoleLevel3) ? config.ApprovalRoleLevel3 : RoleHelper.Director;
            var threshold = config.OutboundDirectorApprovalThreshold > 0 ? config.OutboundDirectorApprovalThreshold : 100000000M;

            var steps = new List<ApprovalStep>();

            if (voucher.Type == "INBOUND")
            {
                if (config.EnableMultiLevelApproval && config.InboundRequireApproval)
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = role2, Status = "PENDING" });
                }
                else
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role2, Status = "PENDING" });
                }
            }
            else if (voucher.Type == "OUTBOUND")
            {
                if (config.AutoApproveLowValueOutbound && config.AutoApproveThreshold > 0 && voucher.TotalAmount <= config.AutoApproveThreshold)
                {
                    // Tự động duyệt đối với phiếu xuất giá trị nhỏ: Không yêu cầu bước duyệt chờ
                    return;
                }

                if (config.EnableMultiLevelApproval)
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = role2, Status = "PENDING" });

                    if (voucher.TotalAmount > threshold)
                    {
                        steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 3, ApproverRole = role3, Status = "PENDING" });
                    }
                }
                else
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role2, Status = "PENDING" });
                    if (voucher.TotalAmount > threshold)
                    {
                        steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = role3, Status = "PENDING" });
                    }
                }
            }
            else if (voucher.Type == "TRANSFER")
            {
                if (config.TransferRequireApproval)
                {
                    if (config.EnableMultiLevelApproval)
                    {
                        steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                        steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = role2, Status = "PENDING" });
                    }
                    else
                    {
                        steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role2, Status = "PENDING" });
                    }
                }
            }
            else
            {
                if (config.EnableMultiLevelApproval)
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role1, Status = "PENDING" });
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 2, ApproverRole = role2, Status = "PENDING" });
                }
                else
                {
                    steps.Add(new ApprovalStep { EntityType = "VOUCHER", EntityId = voucher.VoucherId, Level = 1, ApproverRole = role2, Status = "PENDING" });
                }
            }

            if (steps.Count > 0)
            {
                _db.ApprovalSteps.AddRange(steps);
                await _db.SaveChangesAsync();
            }
        }
    }
}
