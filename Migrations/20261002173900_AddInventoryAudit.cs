using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuAnCode.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryAudits",
                columns: table => new
                {
                    AuditId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AuditNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    WarehouseId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Reason = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAudits", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "InventoryAuditLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuditId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SkuId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BatchNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SystemQuantity = table.Column<int>(type: "int", nullable: false),
                    ActualQuantity = table.Column<int>(type: "int", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    InventoryAuditAuditId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryAuditLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryAuditLines_InventoryAudits_InventoryAuditAuditId",
                        column: x => x.InventoryAuditAuditId,
                        principalTable: "InventoryAudits",
                        principalColumn: "AuditId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryAuditLines_InventoryAuditAuditId",
                table: "InventoryAuditLines",
                column: "InventoryAuditAuditId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryAuditLines");

            migrationBuilder.DropTable(
                name: "InventoryAudits");
        }
    }
}
