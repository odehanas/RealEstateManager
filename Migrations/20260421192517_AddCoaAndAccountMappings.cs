using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RealEstateManager.Migrations
{
    /// <inheritdoc />
    public partial class AddCoaAndAccountMappings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
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

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "Remarks",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PostedBy",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "JournalType",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30);

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumber",
                table: "JournalEntries",
                type: "nvarchar(max)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyAccountSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CompanyId = table.Column<int>(type: "int", nullable: false),
                    DefaultCashAccountId = table.Column<int>(type: "int", nullable: true),
                    AccountsReceivableAccountId = table.Column<int>(type: "int", nullable: true),
                    AccountsPayableAccountId = table.Column<int>(type: "int", nullable: true),
                    AdvancesFromCustomersAccountId = table.Column<int>(type: "int", nullable: true),
                    UnearnedRevenueAccountId = table.Column<int>(type: "int", nullable: true),
                    TenantSecurityDepositLiabilityAccountId = table.Column<int>(type: "int", nullable: true),
                    VatRecoverableAccountId = table.Column<int>(type: "int", nullable: true),
                    VatPayableAccountId = table.Column<int>(type: "int", nullable: true),
                    DueToLandlordsAccountId = table.Column<int>(type: "int", nullable: true),
                    RentalRevenueOwnedAccountId = table.Column<int>(type: "int", nullable: true),
                    ManagementFeeRevenueAccountId = table.Column<int>(type: "int", nullable: true),
                    OtherIncomeAccountId = table.Column<int>(type: "int", nullable: true),
                    DirectExpenseDefaultAccountId = table.Column<int>(type: "int", nullable: true),
                    GeneralExpenseDefaultAccountId = table.Column<int>(type: "int", nullable: true),
                    DepreciationExpenseAccountId = table.Column<int>(type: "int", nullable: true),
                    FinanceCostAccountId = table.Column<int>(type: "int", nullable: true),
                    RetainedEarningsPriorYearsAccountId = table.Column<int>(type: "int", nullable: true),
                    CurrentYearProfitOrLossAccountId = table.Column<int>(type: "int", nullable: true),
                    InvestmentPropertyAccountId = table.Column<int>(type: "int", nullable: true),
                    PdcOnHandAccountId = table.Column<int>(type: "int", nullable: true),
                    PdcContraAccountId = table.Column<int>(type: "int", nullable: true),
                    FixedAssetDefaultAccountId = table.Column<int>(type: "int", nullable: true),
                    AccumulatedDepreciationDefaultAccountId = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyAccountSettings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_AccountsPayableAccountId",
                        column: x => x.AccountsPayableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_AccountsReceivableAccountId",
                        column: x => x.AccountsReceivableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_AccumulatedDepreciationDefaultAccountId",
                        column: x => x.AccumulatedDepreciationDefaultAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_AdvancesFromCustomersAccountId",
                        column: x => x.AdvancesFromCustomersAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_CurrentYearProfitOrLossAccountId",
                        column: x => x.CurrentYearProfitOrLossAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_DefaultCashAccountId",
                        column: x => x.DefaultCashAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_DepreciationExpenseAccountId",
                        column: x => x.DepreciationExpenseAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_DirectExpenseDefaultAccountId",
                        column: x => x.DirectExpenseDefaultAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_DueToLandlordsAccountId",
                        column: x => x.DueToLandlordsAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_FinanceCostAccountId",
                        column: x => x.FinanceCostAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_FixedAssetDefaultAccountId",
                        column: x => x.FixedAssetDefaultAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_GeneralExpenseDefaultAccountId",
                        column: x => x.GeneralExpenseDefaultAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_InvestmentPropertyAccountId",
                        column: x => x.InvestmentPropertyAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_ManagementFeeRevenueAccountId",
                        column: x => x.ManagementFeeRevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_OtherIncomeAccountId",
                        column: x => x.OtherIncomeAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_PdcContraAccountId",
                        column: x => x.PdcContraAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_PdcOnHandAccountId",
                        column: x => x.PdcOnHandAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_RentalRevenueOwnedAccountId",
                        column: x => x.RentalRevenueOwnedAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_RetainedEarningsPriorYearsAccountId",
                        column: x => x.RetainedEarningsPriorYearsAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_TenantSecurityDepositLiabilityAccountId",
                        column: x => x.TenantSecurityDepositLiabilityAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_UnearnedRevenueAccountId",
                        column: x => x.UnearnedRevenueAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_VatPayableAccountId",
                        column: x => x.VatPayableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Accounts_VatRecoverableAccountId",
                        column: x => x.VatRecoverableAccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CompanyAccountSettings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_AccountsPayableAccountId",
                table: "CompanyAccountSettings",
                column: "AccountsPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_AccountsReceivableAccountId",
                table: "CompanyAccountSettings",
                column: "AccountsReceivableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_AccumulatedDepreciationDefaultAccountId",
                table: "CompanyAccountSettings",
                column: "AccumulatedDepreciationDefaultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_AdvancesFromCustomersAccountId",
                table: "CompanyAccountSettings",
                column: "AdvancesFromCustomersAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_CompanyId",
                table: "CompanyAccountSettings",
                column: "CompanyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_CurrentYearProfitOrLossAccountId",
                table: "CompanyAccountSettings",
                column: "CurrentYearProfitOrLossAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_DefaultCashAccountId",
                table: "CompanyAccountSettings",
                column: "DefaultCashAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_DepreciationExpenseAccountId",
                table: "CompanyAccountSettings",
                column: "DepreciationExpenseAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_DirectExpenseDefaultAccountId",
                table: "CompanyAccountSettings",
                column: "DirectExpenseDefaultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_DueToLandlordsAccountId",
                table: "CompanyAccountSettings",
                column: "DueToLandlordsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_FinanceCostAccountId",
                table: "CompanyAccountSettings",
                column: "FinanceCostAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_FixedAssetDefaultAccountId",
                table: "CompanyAccountSettings",
                column: "FixedAssetDefaultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_GeneralExpenseDefaultAccountId",
                table: "CompanyAccountSettings",
                column: "GeneralExpenseDefaultAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_InvestmentPropertyAccountId",
                table: "CompanyAccountSettings",
                column: "InvestmentPropertyAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_ManagementFeeRevenueAccountId",
                table: "CompanyAccountSettings",
                column: "ManagementFeeRevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_OtherIncomeAccountId",
                table: "CompanyAccountSettings",
                column: "OtherIncomeAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_PdcContraAccountId",
                table: "CompanyAccountSettings",
                column: "PdcContraAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_PdcOnHandAccountId",
                table: "CompanyAccountSettings",
                column: "PdcOnHandAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_RentalRevenueOwnedAccountId",
                table: "CompanyAccountSettings",
                column: "RentalRevenueOwnedAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_RetainedEarningsPriorYearsAccountId",
                table: "CompanyAccountSettings",
                column: "RetainedEarningsPriorYearsAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_TenantSecurityDepositLiabilityAccountId",
                table: "CompanyAccountSettings",
                column: "TenantSecurityDepositLiabilityAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_UnearnedRevenueAccountId",
                table: "CompanyAccountSettings",
                column: "UnearnedRevenueAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_VatPayableAccountId",
                table: "CompanyAccountSettings",
                column: "VatPayableAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyAccountSettings_VatRecoverableAccountId",
                table: "CompanyAccountSettings",
                column: "VatRecoverableAccountId");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_AccountingPeriods_AccountingPeriodId",
                table: "JournalEntries",
                column: "AccountingPeriodId",
                principalTable: "AccountingPeriods",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_Companies_CompanyId",
                table: "JournalEntries",
                column: "CompanyId",
                principalTable: "Companies",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntries_JournalEntries_ReversalJournalEntryId",
                table: "JournalEntries",
                column: "ReversalJournalEntryId",
                principalTable: "JournalEntries",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_CustomerInvoices_CustomerInvoiceId",
                table: "JournalEntryLines",
                column: "CustomerInvoiceId",
                principalTable: "CustomerInvoices",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Leases_LeaseId",
                table: "JournalEntryLines",
                column: "LeaseId",
                principalTable: "Leases",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Properties_PropertyId",
                table: "JournalEntryLines",
                column: "PropertyId",
                principalTable: "Properties",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_SecurityDeposits_SecurityDepositId",
                table: "JournalEntryLines",
                column: "SecurityDepositId",
                principalTable: "SecurityDeposits",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Tenants_TenantId",
                table: "JournalEntryLines",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Units_UnitId",
                table: "JournalEntryLines",
                column: "UnitId",
                principalTable: "Units",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_VendorBills_VendorBillId",
                table: "JournalEntryLines",
                column: "VendorBillId",
                principalTable: "VendorBills",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_JournalEntryLines_Vendors_VendorId",
                table: "JournalEntryLines",
                column: "VendorId",
                principalTable: "Vendors",
                principalColumn: "Id");
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

            migrationBuilder.DropTable(
                name: "CompanyAccountSettings");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "JournalEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Remarks",
                table: "JournalEntries",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ReferenceNumber",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PostedBy",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "JournalType",
                table: "JournalEntries",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "DocumentNumber",
                table: "JournalEntries",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)",
                oldNullable: true);

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
    }
}
