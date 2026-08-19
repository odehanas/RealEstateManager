using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateManager.Migrations
{
    /// <summary>
    /// Adds sub-ledger opening balance columns so that Accounts Receivable,
    /// Accounts Payable and Fixed Asset GL control accounts have a
    /// reconcilable detailed source for their opening balances.
    /// </summary>
    public partial class AddOpeningBalanceFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Tenants",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningBalance",
                table: "Vendors",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningCost",
                table: "FixedAssets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "OpeningAccumulatedDepreciation",
                table: "FixedAssets",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "OpeningBalance", table: "Tenants");
            migrationBuilder.DropColumn(name: "OpeningBalance", table: "Vendors");
            migrationBuilder.DropColumn(name: "OpeningCost", table: "FixedAssets");
            migrationBuilder.DropColumn(name: "OpeningAccumulatedDepreciation", table: "FixedAssets");
        }
    }
}
