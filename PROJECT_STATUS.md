# PROJECT STATUS

## Project Path
Current working ASP.NET Core project:

C:\Users\anas\OneDrive - ega.rak.ae\Documents\Applications\RealEstateManager

## Target Stack
- ASP.NET Core MVC
- .NET 8
- Entity Framework Core 8
- SQL Server
- SmartASP hosting target

## Main Objective
Build a real estate accounting ERP with:
- proper journal-based accounting
- monthly revenue recognition
- retained earnings and current year P&L separation
- chart of accounts tree
- account mapping setup
- manual journals
- PDC handling
- fixed assets and depreciation

## Important User Preference
Always provide code as full and final file replacements only.
Do not give partial code insert instructions.

## Accounting Design Agreed
- Ledger-based accounting is required.
- Reports must come from posted journal entries.
- P&L and balance sheet must use the same as-of date.
- Retained earnings must be separated into:
  - prior years retained earnings
  - current year profit/loss
- Revenue must be recognized monthly through accounting entries.
- Unit is used as cost center.
- Optional AR/AP references should exist in journal lines.
- PDC should appear as noted items first and should not affect the tenant real running balance until cleared.

## Completed Work

### 1. Accounting Foundation
Implemented core accounting structure including:
- Company
- Account
- JournalEntry
- JournalEntryLine
- AccountingPeriod
- FiscalYearClosing

### 2. Real Estate Master Data
Implemented:
- Property
- Unit
- Tenant
- Lease

### 3. Operational Transaction Models
Implemented:
- Vendor
- CustomerInvoice
- CustomerReceipt
- VendorBill
- VendorPayment
- SecurityDeposit
- RevenueRecognitionSchedule

### 4. Services
Implemented major accounting services:
- AccountingPostingService
- FinancialReportService
- LedgerInquiryService
- ManualJournalService
- PdcService
- FixedAssetService

### 5. Reports and Inquiry
Implemented:
- Trial Balance
- Profit & Loss
- Balance Sheet
- Journal Inquiry
- General Ledger
- Tenant Statement with separate noted-items PDC section

### 5A. Dashboard
Implemented Home dashboard with:
- operational KPI cards
- occupancy and vacancy metrics
- receivables and overdue metrics
- monthly invoice and receipt trend metrics
- cash KPI from mapped cash account
- landlord liability KPI from due-to-landlords account
- PDC summary and recent activity panels
- quick actions and quick report links
- print-friendly dashboard snapshot
- landlord-focused managed-property drilldown panel

### 5B. Fixed Assets
Implemented in code:
- fixed asset register
- per-asset mapping for:
  - asset account
  - accumulated depreciation account
  - depreciation expense account
- fallback to company account mapping defaults
- monthly straight-line depreciation schedule generation
- monthly depreciation posting into journal entries
- fixed asset register screen
- fixed asset depreciation inquiry screen
- migration file `AddFixedAssets`

### 6. Manual Journals
Implemented manual journal screen with:
- debit and credit lines
- unit as cost center
- optional subledger references

### 7. Navigation Cleanup
Menu reorganized into 4 main groups:
- Setup
- Master Data
- Accounts
- Reports

### 8. Chart of Accounts
Implemented:
- tree view COA
- parent/child accounts
- group vs postable accounts
- account type
- reporting group
- editable account structure

### 9. COA Numbering Structure
Agreed and implemented 5-digit structure with reserved roots:
- 10000 Assets
- 20000 Liabilities
- 30000 Equity
- 40000 Revenue
- 50000 Direct Expenses
- 60000 General Expenses

### 10. Account Mapping
Implemented company-level account mapping using:
- CompanyAccountSetting

Mapped accounts include:
- cash
- AR
- AP
- advances from customers
- unearned revenue
- tenant security deposit liability
- VAT recoverable
- VAT payable
- due to landlords
- rental revenue
- management fee revenue
- other income
- direct expense default
- general expense default
- depreciation expense
- finance cost
- retained earnings
- current year profit/loss
- investment property
- PDC on hand
- PDC contra
- fixed asset default
- accumulated depreciation default

## PDC Work Status

### Batch 1 Completed
Implemented:
- Domain/PdcReceipt.cs
- Services/PdcService.cs
- PDC memorandum accounting
- PDC clearance accounting logic

### PDC Accounting Logic Agreed
On PDC receipt:
- Dr PDC On Hand
- Cr PDC Contra

On PDC clearance:
- reverse memorandum entry
- Dr Cash/Bank
- Cr Advances From Customers or Accounts Receivable depending on settlement mode

### Important Rule
PDC must not affect the tenant real balance until cleared.
Later tenant statement must show PDC in a separate noted-items section only.

## PDC Batch 2 Status
Implemented in code:
- PdcReceiptsController
- PDC Create / Index / Clear views
- PDC view models
- menu link
- DbContext registration
- service registration
- migration file `AddPdcReceipts`

### Resolved Issue 1
Resolved:
- `PdcClearViewModel` now includes the required properties
- PDC view model files are now placed in the correct `Models` folder

`PdcClearViewModel` includes:
- PdcReceiptId
- ChequeNumber
- BankName
- ChequeDate
- ReceivedDate
- ClearedDate
- Amount
- SettlementMode
- LeaseDisplay
- InvoiceDisplay

### Current Known Issue 2
Runtime SQL error:

Invalid object name 'PdcReceipts'

Cause:
- `ApplicationDbContext` contains `DbSet<PdcReceipt>`
- and the migration file now exists
- but the database may still need `dotnet ef database update`

## Immediate Next Steps
1. Apply the existing migrations to the target database:
- dotnet ef database update

2. Test PDC flow:
- open /PdcReceipts
- create PDC
- clear PDC
- confirm journals are posted

3. Test tenant statement:
- open /TenantStatements
- confirm normal balance section shows only real balance movements
- confirm uncleared PDC appears only in the separate noted-items section

4. Review Home dashboard:
- confirm KPI cards load correctly
- confirm cash and landlord KPIs reflect mapped accounts
- confirm recent activity panels show expected records

5. Test fixed assets:
- open /FixedAssets
- create a fixed asset using mapped accounts
- confirm depreciation schedules are generated
- run monthly depreciation posting
- confirm journal entries are posted correctly

## Future Planned Work
- closed-period validation improvements
- print-friendly reports
- roles and permissions
- role-based dashboard sections after roles and permissions are implemented
- additional dashboard refinements and advanced KPIs

## Notes For Continuation
- User is not a programmer.
- Always send complete final files.
- Avoid partial insert instructions.
- The real project path is the OneDrive project path, not the old mixed repo path.
- If tool access is tied to the wrong root, continue by providing full files for manual copy/paste.

## Current Priority
Apply the existing PDC and Fixed Assets migrations to the database, then test PDC, tenant statement, fixed asset register, and monthly depreciation posting flows.
