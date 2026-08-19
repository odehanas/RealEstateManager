using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateManager.Migrations
{
    /// <inheritdoc />
    public partial class ReviseChartOfAccountsToFiveDigits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsGroup",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPostable",
                table: "Accounts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ReportingGroup",
                table: "Accounts",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "SortOrder",
                table: "Accounts",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsGroup",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "IsPostable",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "ReportingGroup",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "SortOrder",
                table: "Accounts");
        }
    }
}
