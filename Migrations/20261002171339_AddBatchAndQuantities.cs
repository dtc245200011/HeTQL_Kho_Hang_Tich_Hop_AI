using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DuAnCode.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBatchAndQuantities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockLedgers",
                table: "StockLedgers");

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "VoucherLines",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "DocumentQuantity",
                table: "VoucherLines",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "VoucherLines",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BatchNumber",
                table: "StockLedgers",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiryDate",
                table: "StockLedgers",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceDocument",
                table: "InventoryVouchers",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockLedgers",
                table: "StockLedgers",
                columns: new[] { "SkuId", "WarehouseId", "Status", "BatchNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_StockLedgers",
                table: "StockLedgers");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "DocumentQuantity",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "VoucherLines");

            migrationBuilder.DropColumn(
                name: "BatchNumber",
                table: "StockLedgers");

            migrationBuilder.DropColumn(
                name: "ExpiryDate",
                table: "StockLedgers");

            migrationBuilder.DropColumn(
                name: "ReferenceDocument",
                table: "InventoryVouchers");

            migrationBuilder.AddPrimaryKey(
                name: "PK_StockLedgers",
                table: "StockLedgers",
                columns: new[] { "SkuId", "WarehouseId", "Status" });
        }
    }
}
