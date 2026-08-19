using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateManager.Migrations
{
    /// <inheritdoc />
    public partial class EnhanceJournalModelForManualEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CustomerInvoiceId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LeaseId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PropertyId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SecurityDepositId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TenantId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UnitId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorBillId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VendorId",
                table: "JournalEntryLines",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccountingPeriodId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CompanyId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNumber",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "JournalType",
                table: "JournalEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "PostedAt",
                table: "JournalEntries",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostedBy",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Remarks",
                table: "JournalEntries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReversalJournalEntryId",
                table: "JournalEntries",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "JournalEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_CustomerInvoiceId",
                table: "JournalEntryLines",
                column: "CustomerInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_LeaseId",
                table: "JournalEntryLines",
                column: "LeaseId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_PropertyId",
                table: "JournalEntryLines",
                column: "PropertyId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_SecurityDepositId",
                table: "JournalEntryLines",
                column: "SecurityDepositId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_TenantId",
                table: "JournalEntryLines",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_UnitId",
                table: "JournalEntryLines",
                column: "UnitId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_VendorBillId",
                table: "JournalEntryLines",
                column: "VendorBillId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntryLines_VendorId",
                table: "JournalEntryLines",
                column: "VendorId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_AccountingPeriodId",
                table: "JournalEntries",
                column: "AccountingPeriodId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_CompanyId",
                table: "JournalEntries",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_JournalEntries_ReversalJournalEntryId",
                table: "JournalEntries",
                column: "ReversalJournalEntryId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_AccountingPeriods_AccountingPeriodId",
                table: "JournalEntries",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Companies_CompanyId",
                table: "JournalEntries",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_JournalEntries_ReversalJournalEntryId",
                table: "JournalEntries",
                column: "ReversalJournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_CustomerInvoices_CustomerInvoiceId",
                table: "JournalEntryLines",
                column: "CustomerInvoiceId",
                principalTable: "CustomerInvoices",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Leases_LeaseId",
                table: "JournalEntryLines",
                column: "LeaseId",
                principalTable: "Leases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Properties_PropertyId",
                table: "JournalEntryLines",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_SecurityDeposits_SecurityDepositId",
                table: "JournalEntryLines",
                column: "SecurityDepositId",
                principalTable: "SecurityDeposits",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Tenants_TenantId",
                table: "JournalEntryLines",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Units_UnitId",
                table: "JournalEntryLines",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_VendorBills_VendorBillId",
                table: "JournalEntryLines",
                column: "VendorBillId",
                principalTable: "VendorBills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Vendors_VendorId",
                table: "JournalEntryLines",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_AccountingPeriods_AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_Companies_CompanyId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntries_JournalEntries_ReversalJournalEntryId",
                table: "JournalEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_CustomerInvoices_CustomerInvoiceId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Leases_LeaseId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Properties_PropertyId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_SecurityDeposits_SecurityDepositId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Tenants_TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Units_UnitId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_VendorBills_VendorBillId",
                table: "JournalEntryLines");

            migrationBuilder.DropForeignKey(
                name: "FK_JournalEntryLines_Vendors_VendorId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_CustomerInvoiceId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_LeaseId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_PropertyId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_SecurityDepositId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_UnitId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_VendorBillId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntryLines_VendorId",
                table: "JournalEntryLines");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_CompanyId",
                table: "JournalEntries");

            migrationBuilder.DropIndex(
                name: "IX_JournalEntries_ReversalJournalEntryId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "CustomerInvoiceId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "LeaseId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "SecurityDepositId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "TenantId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "UnitId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "VendorBillId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "VendorId",
                table: "JournalEntryLines");

            migrationBuilder.DropColumn(
                name: "AccountingPeriodId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "CompanyId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "DocumentNumber",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "JournalType",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "PostedAt",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "PostedBy",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "Remarks",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "ReversalJournalEntryId",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "JournalEntries");
        }
    }
}
