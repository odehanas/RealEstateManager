using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class DiagnosticsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _postingService;
        private readonly FinancialReportService _financialReportService;
        private readonly OpeningBalanceService _openingBalanceService;

        public DiagnosticsController(
            ApplicationDbContext db,
            AccountingPostingService postingService,
            FinancialReportService financialReportService,
            OpeningBalanceService openingBalanceService)
        {
            _db = db;
            _postingService = postingService;
            _financialReportService = financialReportService;
            _openingBalanceService = openingBalanceService;
        }

        [HttpGet]
        public async Task<IActionResult> SeedDemoData()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Code == "PROP-001");
            if (property == null)
            {
                property = new Property
                {
                    CompanyId = company.Id,
                    Code = "PROP-001",
                    Name = "Palm Residence",
                    Address = "Dubai Marina",
                    PropertyType = "Residential",
                    OwnershipType = "Owned",
                    AssetValue = 5000000m,
                    CommissionPercentage = 0m,
                    IsActive = true
                };

                _db.Properties.Add(property);
                await _db.SaveChangesAsync();
            }

            var unit = await _db.Units.FirstOrDefaultAsync(x => x.PropertyId == property.Id && x.UnitNumber == "A-101");
            if (unit == null)
            {
                unit = new Unit
                {
                    PropertyId = property.Id,
                    UnitNumber = "A-101",
                    Floor = "1",
                    UsageType = "Residential",
                    AreaSqFt = 1200m,
                    StandardAnnualRent = 120000m,
                    Status = "Occupied",
                    IsActive = true
                };

                _db.Units.Add(unit);
                await _db.SaveChangesAsync();
            }

            var tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Code == "TEN-001");
            if (tenant == null)
            {
                tenant = new Tenant
                {
                    CompanyId = company.Id,
                    Code = "TEN-001",
                    FullName = "Demo Tenant",
                    Email = "tenant@example.com",
                    Phone = "0500000000",
                    Nationality = "UAE",
                    IdNumber = "784-XXXX-XXXXXXX-X",
                    Address = "Dubai",
                    IsActive = true
                };

                _db.Tenants.Add(tenant);
                await _db.SaveChangesAsync();
            }

            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.ContractNumber == "LEASE-001");
            if (lease == null)
            {
                lease = new Lease
                {
                    CompanyId = company.Id,
                    UnitId = unit.Id,
                    TenantId = tenant.Id,
                    ContractNumber = "LEASE-001",
                    StartDate = new DateTime(DateTime.Today.Year, 1, 1),
                    EndDate = new DateTime(DateTime.Today.Year, 12, 31),
                    AnnualRent = 120000m,
                    BillingFrequencyMonths = 1,
                    IsVatApplicable = false,
                    VatRate = 0m,
                    SecurityDepositAmount = 10000m,
                    Status = "Active",
                    Notes = "Demo lease",
                    CreatedAt = DateTime.UtcNow
                };

                _db.Leases.Add(lease);
                await _db.SaveChangesAsync();
            }

            var vendor = await _db.Vendors.FirstOrDefaultAsync(x => x.Code == "VEN-001");
            if (vendor == null)
            {
                vendor = new Vendor
                {
                    CompanyId = company.Id,
                    Code = "VEN-001",
                    Name = "Demo Maintenance Vendor",
                    ServiceType = "Maintenance",
                    ContactPerson = "Support Team",
                    Email = "vendor@example.com",
                    Phone = "0501111111",
                    IsActive = true
                };

                _db.Vendors.Add(vendor);
                await _db.SaveChangesAsync();
            }

            var invoice = await _db.CustomerInvoices.FirstOrDefaultAsync(x => x.InvoiceNumber == "INV-001");
            if (invoice == null)
            {
                invoice = new CustomerInvoice
                {
                    CompanyId = company.Id,
                    LeaseId = lease.Id,
                    InvoiceNumber = "INV-001",
                    InvoiceDate = DateTime.Today,
                    DueDate = DateTime.Today,
                    Subtotal = 10000m,
                    VatAmount = 0m,
                    TotalAmount = 10000m,
                    AmountPaid = 0m,
                    Status = "Draft"
                };

                _db.CustomerInvoices.Add(invoice);
                await _db.SaveChangesAsync();
            }

            var receipt = await _db.CustomerReceipts.FirstOrDefaultAsync(x => x.ReceiptNumber == "REC-001");
            if (receipt == null)
            {
                receipt = new CustomerReceipt
                {
                    CompanyId = company.Id,
                    CustomerInvoiceId = invoice.Id,
                    ReceiptNumber = "REC-001",
                    ReceiptDate = DateTime.Today,
                    Amount = 10000m,
                    PaymentMethod = "Bank"
                };

                _db.CustomerReceipts.Add(receipt);
                await _db.SaveChangesAsync();
            }

            var vendorBill = await _db.VendorBills.FirstOrDefaultAsync(x => x.BillNumber == "BILL-001");
            if (vendorBill == null)
            {
                vendorBill = new VendorBill
                {
                    CompanyId = company.Id,
                    VendorId = vendor.Id,
                    PropertyId = property.Id,
                    BillNumber = "BILL-001",
                    BillDate = DateTime.Today,
                    DueDate = DateTime.Today,
                    Subtotal = 1500m,
                    VatAmount = 75m,
                    TotalAmount = 1575m,
                    AmountPaid = 0m,
                    ExpenseCategory = "Property Maintenance",
                    Status = "Draft"
                };

                _db.VendorBills.Add(vendorBill);
                await _db.SaveChangesAsync();
            }

            var vendorPayment = await _db.VendorPayments.FirstOrDefaultAsync(x => x.PaymentNumber == "VPAY-001");
            if (vendorPayment == null)
            {
                vendorPayment = new VendorPayment
                {
                    CompanyId = company.Id,
                    VendorBillId = vendorBill.Id,
                    PaymentNumber = "VPAY-001",
                    PaymentDate = DateTime.Today,
                    Amount = 1575m,
                    PaymentMethod = "Bank"
                };

                _db.VendorPayments.Add(vendorPayment);
                await _db.SaveChangesAsync();
            }

            var deposit = await _db.SecurityDeposits.FirstOrDefaultAsync(x => x.ReferenceNumber == "DEP-001");
            if (deposit == null)
            {
                deposit = new SecurityDeposit
                {
                    CompanyId = company.Id,
                    LeaseId = lease.Id,
                    ReferenceNumber = "DEP-001",
                    ReceiptDate = DateTime.Today,
                    Amount = 10000m,
                    PaymentMethod = "Bank",
                    Status = "Held"
                };

                _db.SecurityDeposits.Add(deposit);
                await _db.SaveChangesAsync();
            }

            var recognitionDate = new DateTime(DateTime.Today.Year, DateTime.Today.Month, DateTime.DaysInMonth(DateTime.Today.Year, DateTime.Today.Month));
            var schedule = await _db.RevenueRecognitionSchedules.FirstOrDefaultAsync(x => x.LeaseId == lease.Id && x.RecognitionDate == recognitionDate);

            if (schedule == null)
            {
                schedule = new RevenueRecognitionSchedule
                {
                    CompanyId = company.Id,
                    LeaseId = lease.Id,
                    RecognitionDate = recognitionDate,
                    Amount = 10000m,
                    Status = "Planned"
                };

                _db.RevenueRecognitionSchedules.Add(schedule);
                await _db.SaveChangesAsync();
            }

            return Json(new
            {
                message = "Demo data created or already exists.",
                companyId = company.Id,
                propertyId = property.Id,
                unitId = unit.Id,
                tenantId = tenant.Id,
                leaseId = lease.Id,
                vendorId = vendor.Id
            });
        }

        [HttpGet]
        public async Task<IActionResult> PostDemoTransactions(DateTime? asOfDate)
        {
            var cutoffDate = (asOfDate ?? DateTime.Today).Date;

            var invoiceIds = await _db.CustomerInvoices
                .Where(x => !x.JournalEntryId.HasValue && x.InvoiceDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in invoiceIds)
                await _postingService.PostCustomerInvoiceAsync(id);

            var receiptIds = await _db.CustomerReceipts
                .Where(x => !x.JournalEntryId.HasValue && x.ReceiptDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in receiptIds)
                await _postingService.PostCustomerReceiptAsync(id);

            var billIds = await _db.VendorBills
                .Where(x => !x.JournalEntryId.HasValue && x.BillDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in billIds)
                await _postingService.PostVendorBillAsync(id);

            var paymentIds = await _db.VendorPayments
                .Where(x => !x.JournalEntryId.HasValue && x.PaymentDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in paymentIds)
                await _postingService.PostVendorPaymentAsync(id);

            var depositIds = await _db.SecurityDeposits
                .Where(x => !x.JournalEntryId.HasValue && x.ReceiptDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in depositIds)
                await _postingService.PostSecurityDepositAsync(id);

            var scheduleIds = await _db.RevenueRecognitionSchedules
                .Where(x => !x.JournalEntryId.HasValue && x.Status == "Planned" && x.RecognitionDate <= cutoffDate)
                .OrderBy(x => x.Id)
                .Select(x => x.Id)
                .ToListAsync();

            foreach (var id in scheduleIds)
                await _postingService.PostRevenueRecognitionAsync(id);

            return Json(new
            {
                message = "All due demo transactions up to the selected date have been posted.",
                cutoffDate
            });
        }

        [HttpGet]
        public async Task<IActionResult> TrialBalance(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var report = await _financialReportService.GetTrialBalanceAsync(companyId, (asOfDate ?? DateTime.Today).Date);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> ProfitAndLoss(int? fiscalYear, DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var cutoffDate = (asOfDate ?? DateTime.Today).Date;
            var year = fiscalYear ?? cutoffDate.Year;

            var startDate = new DateTime(year, 1, 1);
            var endDate = cutoffDate.Year == year
                ? cutoffDate
                : new DateTime(year, 12, 31);

            var report = await _financialReportService.GetProfitAndLossAsync(company.Id, startDate, endDate);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> BalanceSheet(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var report = await _financialReportService.GetBalanceSheetAsync(company.Id, (asOfDate ?? DateTime.Today).Date);
            return Json(report);
        }

        [HttpGet]
        public async Task<IActionResult> ComparePnLAndBalanceSheet(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var cutoffDate = (asOfDate ?? DateTime.Today).Date;
            var startDate = new DateTime(cutoffDate.Year, 1, 1);

            var pnl = await _financialReportService.GetProfitAndLossAsync(company.Id, startDate, cutoffDate);
            var balanceSheet = await _financialReportService.GetBalanceSheetAsync(company.Id, cutoffDate);

            return Json(new
            {
                asOfDate = cutoffDate,
                pnlNetProfitOrLoss = pnl.NetProfitOrLoss,
                balanceSheetCurrentYearProfitOrLoss = balanceSheet.CurrentYearProfitOrLoss,
                difference = pnl.NetProfitOrLoss - balanceSheet.CurrentYearProfitOrLoss,
                isMatching = pnl.NetProfitOrLoss == balanceSheet.CurrentYearProfitOrLoss
            });
        }

        [HttpGet]
        public async Task<IActionResult> CloseYear(int fiscalYear)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var journal = await _postingService.CloseFiscalYearAsync(company.Id, fiscalYear);

            return Json(new
            {
                message = $"Fiscal year {fiscalYear} closed successfully.",
                journalEntryId = journal.Id,
                journal.EntryNumber
            });
        }

        [HttpGet]
        public async Task<IActionResult> UnbalancedJournals(DateTime? asOfDate)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var cutoffDate = (asOfDate ?? DateTime.Today).Date;

            var entries = await _db.JournalEntries
                .AsNoTracking()
                .Include(x => x.Lines)
                .Where(x => x.CompanyId == companyId && x.IsPosted && x.EntryDate <= cutoffDate)
                .ToListAsync();

            var unbalanced = entries
                .Select(x => new
                {
                    x.Id,
                    x.EntryNumber,
                    x.EntryDate,
                    x.Description,
                    x.SourceType,
                    TotalDebit = x.Lines.Sum(l => l.Debit),
                    TotalCredit = x.Lines.Sum(l => l.Credit),
                    Difference = x.Lines.Sum(l => l.Debit) - x.Lines.Sum(l => l.Credit)
                })
                .Where(x => x.Difference != 0m)
                .OrderBy(x => x.EntryDate)
                .ToList();

            return Json(new
            {
                asOfDate = cutoffDate,
                count = unbalanced.Count,
                totalDifference = unbalanced.Sum(x => x.Difference),
                entries = unbalanced
            });
        }

        [HttpGet]
        public async Task<IActionResult> OpeningBalanceCheck()
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
                return BadRequest("No company record found.");

            var reconciliation = await _openingBalanceService.GetReconciliationAsync();
            var difference = reconciliation.TotalDebitOpeningBalance - reconciliation.TotalCreditOpeningBalance;

            return Json(new
            {
                companyId,
                companyName = company.Name,
                totalDebit = reconciliation.TotalDebitOpeningBalance,
                totalCredit = reconciliation.TotalCreditOpeningBalance,
                difference,
                isBalanced = difference == 0m
            });
        }
    }
}
