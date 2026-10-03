using DuAnCode.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DuAnCode.Web.Data
{
    public class ApplicationDbContext : IdentityDbContext<User, Role, string>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        public DbSet<Warehouse> Warehouses { get; set; } = null!;
        public DbSet<Supplier> Suppliers { get; set; } = null!;
        public DbSet<ProductModel> ProductModels { get; set; } = null!;
        public DbSet<SkuVariant> SkuVariants { get; set; } = null!;
        public DbSet<StockLedger> StockLedgers { get; set; } = null!;
        public DbSet<StockMovement> StockMovements { get; set; } = null!;
        public DbSet<PurchaseOrder> PurchaseOrders { get; set; } = null!;
        public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; } = null!;
        public DbSet<SalesOrder> SalesOrders { get; set; } = null!;
        public DbSet<SalesOrderLine> SalesOrderLines { get; set; } = null!;
        public DbSet<ComboProduct> ComboProducts { get; set; } = null!;
        public DbSet<BomComponent> BomComponents { get; set; } = null!;
        public DbSet<StockTransfer> StockTransfers { get; set; } = null!;
        public DbSet<StockTransferLine> StockTransferLines { get; set; } = null!;
        public DbSet<StockAdjustment> StockAdjustments { get; set; } = null!;
        public DbSet<DamagedRecord> DamagedRecords { get; set; } = null!;
        public DbSet<ApprovalStep> ApprovalSteps { get; set; } = null!;
        public DbSet<AiSuggestion> AiSuggestions { get; set; } = null!;
        public DbSet<AuditLog> AuditLogs { get; set; } = null!;
        public DbSet<InventoryVoucher> InventoryVouchers { get; set; } = null!;
        public DbSet<VoucherLine> VoucherLines { get; set; } = null!;
        public DbSet<SystemConfig> SystemConfigs { get; set; } = null!;
        public DbSet<InventoryAudit> InventoryAudits { get; set; } = null!;
        public DbSet<InventoryAuditLine> InventoryAuditLines { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Warehouse>(b =>
            {
                b.HasKey(w => w.WarehouseId);
                b.Property(w => w.WarehouseId).HasMaxLength(50);
                b.Property(w => w.WarehouseName).HasMaxLength(100).IsRequired();
                b.Property(w => w.MaxCapacityCbm).HasPrecision(18, 6);
            });

            modelBuilder.Entity<Supplier>(b =>
            {
                b.HasKey(s => s.SupplierId);
                b.Property(s => s.SupplierId).HasMaxLength(50);
                b.Property(s => s.TaxCode).HasMaxLength(50).IsRequired();
                b.Property(s => s.Name).HasMaxLength(200).IsRequired();
            });

            modelBuilder.Entity<ProductModel>(b =>
            {
                b.HasKey(p => p.ProductModelId);
                b.Property(p => p.ProductName).HasMaxLength(200).IsRequired();
                b.Property(p => p.MinStock).HasDefaultValue(0);
            });

            modelBuilder.Entity<SkuVariant>(b =>
            {
                b.HasKey(s => s.SkuId);
                b.Property(s => s.SkuId).HasMaxLength(100);
                b.HasOne<ProductModel>().WithMany().HasForeignKey(s => s.ProductModelId).OnDelete(DeleteBehavior.Restrict);
                b.Property(s => s.Cbm).HasPrecision(18, 6);
                b.Property(s => s.UnitPrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<ComboProduct>(b =>
            {
                b.HasKey(c => c.ComboId);
                b.Property(c => c.ComboId).HasMaxLength(50);
                b.Property(c => c.ComboName).HasMaxLength(200).IsRequired();
                b.Property(c => c.CbmOverride).HasPrecision(18, 6);
            });

            modelBuilder.Entity<PurchaseOrder>(b =>
            {
                b.HasKey(p => p.PoId);
                b.Property(p => p.PoId).HasMaxLength(100);
                b.Property(p => p.TotalAmount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<PurchaseOrderLine>(b =>
            {
                b.HasKey(l => l.Id);
                b.Property(l => l.UnitCost).HasPrecision(18, 2);
            });

            modelBuilder.Entity<SalesOrder>(b =>
            {
                b.HasKey(s => s.SoId);
                b.Property(s => s.SoId).HasMaxLength(100);
                b.Property(s => s.TotalAmount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<SalesOrderLine>(b =>
            {
                b.HasKey(l => l.LineId);
                b.Property(l => l.UnitPrice).HasPrecision(18, 2);
            });

            modelBuilder.Entity<StockLedger>(b =>
            {
                b.HasKey(x => new { x.SkuId, x.WarehouseId, x.Status, x.BatchNumber });
                b.Property(x => x.Status).HasMaxLength(20);
                b.Property(x => x.BatchNumber).HasMaxLength(100);
                b.Property(x => x.Quantity).IsRequired();
            });

            modelBuilder.Entity<StockMovement>(b =>
            {
                b.HasKey(m => m.MovementId);
                b.Property(m => m.MovementId).ValueGeneratedOnAdd();
                b.Property(m => m.StockStatus).HasMaxLength(20);
                b.Property(m => m.MovementType).HasMaxLength(30);
                b.Property(m => m.ReferenceType).HasMaxLength(50);
                b.Property(m => m.ReferenceId).HasMaxLength(100);
                b.Property(m => m.PerformedBy).HasMaxLength(50);
                b.Property(m => m.CreatedAt).HasDefaultValueSql("GETUTCDATE()");
            });

            modelBuilder.Entity<InventoryVoucher>(b =>
            {
                b.HasKey(v => v.VoucherId);
                b.Property(v => v.VoucherNumber).HasMaxLength(100);
                b.Property(v => v.Type).HasMaxLength(20);
                b.Property(v => v.Counterparty).HasMaxLength(200);
                b.Property(v => v.Address).HasMaxLength(300);
                b.Property(v => v.Reason).HasMaxLength(500);
                b.Property(v => v.TotalAmount).HasPrecision(18, 2);
            });

            modelBuilder.Entity<VoucherLine>(b =>
            {
                b.HasKey(l => l.Id);
                b.Property(l => l.SkuId).HasMaxLength(100);
                b.Property(l => l.SkuCode).HasMaxLength(100);
                b.Property(l => l.ProductName).HasMaxLength(300);
                b.Property(l => l.Unit).HasMaxLength(50);
                b.Property(l => l.UnitPrice).HasPrecision(18, 2);
                b.Property(l => l.LineTotal).HasPrecision(18, 2);
            });
        }
    }
}
