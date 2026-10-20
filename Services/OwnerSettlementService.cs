using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Settling with the landlords of managed properties. We collect their rent, keep our
    /// commission and pay the rest over. The statement works per contract:
    ///
    ///     rent billed to the tenant (before VAT, after credit notes)
    ///   - our commission at the property's commission %
    ///   - 5% VAT on that commission (when we are VAT registered)
    ///   = the owner's share
    ///   - what we have already paid the owner
    ///   = the balance still owed, paid now in full or in part.
    ///
    /// Only posted documents count, as everywhere else in the accounts. The owner's share
    /// of rent actually COLLECTED is shown next to it, because that is the money we
    /// hold - paying beyond it means advancing the owner rent the tenant still owes us.
    ///
    /// Each payment posts  Dr Due to Landlords / Cr the bank or cash account paid from.
    /// </summary>
    public class OwnerSettlementService
    {
        public const string SourceType = "OwnerSettlement";

        public static readonly string[] PaymentMethods = { "Bank Transfer", "Cheque", "Cash" };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public OwnerSettlementService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>The landlords named on the company's managed properties.</summary>
        public async Task<List<string>> GetOwnersAsync()
        {
            var names = await _db.Properties.AsNoTracking()
                .Where(x => x.CompanyId == CompanyId && x.OwnershipType == "Managed" && x.OwnerName != null)
                .Select(x => x.OwnerName!)
                .ToListAsync();

            return names
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(x => x)
                .ToList();
        }

        /// <summary>The cash and bank accounts a settlement may be paid from.</summary>
        public async Task<List<Account>> GetPaymentAccountsAsync()
        {
            var defaultCashId = await _db.CompanyAccountSettings.AsNoTracking()
                .Select(x => x.DefaultCashAccountId)
                .FirstOrDefaultAsync();

            return await _db.Accounts.AsNoTracking()
                .Where(x => x.AccountType == "Asset" && x.IsPostable && x.IsActive && !x.IsGroup &&
                            (x.Id == defaultCashId || x.IsPettyCashAccount || x.Code.StartsWith("111")))
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        public async Task<OwnerSettlementIndexViewModel> GetOverviewAsync(DateTime asOfDate)
        {
            var model = new OwnerSettlementIndexViewModel { AsOfDate = asOfDate.Date };

            foreach (var owner in await GetOwnersAsync())
            {
                var statement = await BuildStatementAsync(owner, asOfDate);
                model.Owners.Add(new OwnerSettlementSummaryRow
                {
                    OwnerName = owner,
                    PropertyCount = statement.PropertyCount,
                    ContractCount = statement.Contracts.Count,
                    RentBilled = statement.RentBilled,
                    Commission = statement.Commission,
                    CommissionVat = statement.CommissionVat,
                    OwnerShare = statement.OwnerShare,
                    PreviouslySettled = statement.PreviouslySettled,
                    BalancePayable = statement.BalancePayable,
                    AvailableFromCollections = statement.AvailableFromCollections
                });
            }

            model.RecentSettlements = await _db.OwnerSettlements.AsNoTracking()
                .Include(x => x.JournalEntry)
                .Include(x => x.PaidFromAccount)
                .OrderByDescending(x => x.SettlementDate).ThenByDescending(x => x.Id)
                .Take(50)
                .ToListAsync();

            return model;
        }

        public async Task<OwnerSettlementStatementViewModel> BuildStatementAsync(string ownerName, DateTime asOfDate)
        {
            var asOf = asOfDate.Date;
            var owner = (ownerName ?? string.Empty).Trim();
            var companyId = CompanyId;

            var company = await _db.Companies.AsNoTracking().FirstAsync(x => x.Id == companyId);
            var vatRate = OwnerCommissionInvoiceService.CommissionVatRate(company);

            var properties = (await _db.Properties.AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.OwnershipType == "Managed" && x.OwnerName != null)
                    .ToListAsync())
                .Where(x => string.Equals(x.OwnerName!.Trim(), owner, StringComparison.OrdinalIgnoreCase))
                .ToList();

            var model = new OwnerSettlementStatementViewModel
            {
                OwnerName = owner,
                AsOfDate = asOf,
                CompanyName = company.Name,
                CommissionVatRate = vatRate,
                PropertyCount = properties.Count
            };

            if (properties.Count == 0)
                return model;

            var propertyIds = properties.Select(x => x.Id).ToList();

            var leases = await _db.Leases.AsNoTracking()
                .Include(x => x.Unit)
                .Include(x => x.Tenant)
                .Where(x => x.Unit != null && propertyIds.Contains(x.Unit.PropertyId) &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval &&
                            x.StartDate <= asOf)
                .ToListAsync();

            var leaseIds = leases.Select(x => x.Id).ToList();

            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => leaseIds.Contains(x.LeaseId) &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.Status != "Cancelled" &&
                            x.InvoiceDate <= asOf)
                .GroupBy(x => x.LeaseId)
                .Select(g => new { LeaseId = g.Key, Net = g.Sum(x => x.Subtotal), Gross = g.Sum(x => x.TotalAmount) })
                .ToListAsync();

            var credits = await _db.CustomerCreditNotes.AsNoTracking()
                .Where(x => x.CustomerInvoice != null && leaseIds.Contains(x.CustomerInvoice.LeaseId) &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.CreditNoteDate <= asOf)
                .GroupBy(x => x.CustomerInvoice!.LeaseId)
                .Select(g => new { LeaseId = g.Key, Net = g.Sum(x => x.Subtotal), Gross = g.Sum(x => x.TotalAmount) })
                .ToListAsync();

            var receipts = await _db.CustomerReceipts.AsNoTracking()
                .Where(x => x.CustomerInvoice != null && leaseIds.Contains(x.CustomerInvoice.LeaseId) &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted &&
                            x.ReceiptDate <= asOf)
                .GroupBy(x => x.CustomerInvoice!.LeaseId)
                .Select(g => new { LeaseId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToListAsync();

            // Cleared cheques count as collected whether they settled an invoice or were
            // held as an advance; collections are capped at what was billed below.
            var cheques = await _db.PdcReceipts.AsNoTracking()
                .Where(x => leaseIds.Contains(x.LeaseId) &&
                            x.ClearanceJournalEntry != null && x.ClearanceJournalEntry.IsPosted &&
                            x.ClearedDate != null && x.ClearedDate <= asOf)
                .GroupBy(x => x.LeaseId)
                .Select(g => new { LeaseId = g.Key, Amount = g.Sum(x => x.Amount) })
                .ToListAsync();

            foreach (var lease in leases)
            {
                var property = properties.First(p => p.Id == lease.Unit!.PropertyId);
                var pct = property.CommissionPercentage;

                var invoice = invoices.FirstOrDefault(x => x.LeaseId == lease.Id);
                var credit = credits.FirstOrDefault(x => x.LeaseId == lease.Id);

                var billedNet = (invoice?.Net ?? 0m) - (credit?.Net ?? 0m);
                var billedGross = (invoice?.Gross ?? 0m) - (credit?.Gross ?? 0m);

                var collectedGross = (receipts.FirstOrDefault(x => x.LeaseId == lease.Id)?.Amount ?? 0m) +
                                     (cheques.FirstOrDefault(x => x.LeaseId == lease.Id)?.Amount ?? 0m);
                collectedGross = Math.Max(0m, Math.Min(collectedGross, billedGross));

                // Receipts include VAT, which is ours to pay to the FTA and never the owner's.
                var collectedNet = billedGross == 0m
                    ? 0m
                    : Math.Round(collectedGross * billedNet / billedGross, 2);

                var commission = Math.Round(billedNet * pct / 100m, 2);
                var collectedCommission = Math.Round(collectedNet * pct / 100m, 2);

                // Our commission carries 5% VAT, which comes out of the owner's money too.
                var commissionVat = VatDeterminationService.Round(commission * vatRate / 100m);
                var collectedCommissionVat = VatDeterminationService.Round(collectedCommission * vatRate / 100m);

                // A contract that is over and was never billed has nothing to settle.
                if (billedNet == 0m && lease.EndDate < asOf)
                    continue;

                model.Contracts.Add(new OwnerSettlementContractRow
                {
                    LeaseId = lease.Id,
                    PropertyCode = property.Code,
                    PropertyName = property.Name,
                    UnitNumber = lease.Unit!.UnitNumber,
                    ContractNumber = lease.ContractNumber,
                    TenantName = lease.Tenant?.FullName ?? string.Empty,
                    StartDate = lease.StartDate,
                    EndDate = lease.EndDate,
                    Status = LeaseStatuses.Describe(lease.Status),
                    AnnualRent = lease.AnnualRent,
                    AgreementRent = AgreementRent(lease),
                    CommissionPercentage = pct,
                    RentBilled = billedNet,
                    RentCollected = collectedNet,
                    Commission = commission,
                    CommissionVat = commissionVat,
                    OwnerShare = billedNet - commission - commissionVat,
                    OwnerShareCollected = collectedNet - collectedCommission - collectedCommissionVat
                });
            }

            model.Contracts = model.Contracts
                .OrderBy(x => x.PropertyCode).ThenBy(x => x.UnitNumber).ThenBy(x => x.StartDate)
                .ToList();

            var settlements = await _db.OwnerSettlements.AsNoTracking()
                .Include(x => x.JournalEntry)
                .Include(x => x.PaidFromAccount)
                .Where(x => x.OwnerName == owner)
                .OrderBy(x => x.SettlementDate).ThenBy(x => x.Id)
                .ToListAsync();

            model.Settlements = settlements;
            model.PreviouslySettled = settlements
                .Where(x => x.IsPosted && x.SettlementDate <= asOf)
                .Sum(x => x.AmountPaid);

            // Anything not yet counted above - drafts, or paid after the cut-off - still
            // reduces what may be paid now, so an owner is never paid twice.
            model.PendingSettlements = settlements
                .Where(x => !(x.IsPosted && x.SettlementDate <= asOf))
                .Sum(x => x.AmountPaid);

            return model;
        }

        /// <summary>
        /// Records a payment to an owner and posts it. Returns the settlement id and, if the
        /// journal could not be posted straight away, the reason (the settlement is then
        /// saved with a draft journal to post from Journal Inquiry).
        /// </summary>
        public async Task<(int SettlementId, string? PostingWarning)> PayAsync(
            OwnerSettlementPaymentInput input, int? userId, string? userName)
        {
            if (input.Amount <= 0m)
                throw new InvalidOperationException("Enter an amount greater than zero.");

            var amount = Math.Round(input.Amount, 2);
            var statement = await BuildStatementAsync(input.OwnerName, input.AsOfDate);

            if (!statement.HasProperties)
                throw new InvalidOperationException("No managed property is recorded for this owner.");

            if (amount > statement.MaximumPayable)
            {
                throw new InvalidOperationException(
                    $"The most that can be paid to {statement.OwnerName} is {statement.MaximumPayable:N2} " +
                    "(the owner's share less what was already paid or is waiting to be posted).");
            }

            var paymentAccounts = await GetPaymentAccountsAsync();
            if (paymentAccounts.All(x => x.Id != input.PaidFromAccountId))
                throw new InvalidOperationException("Choose the bank or cash account the owner is paid from.");

            if (!PaymentMethods.Contains(input.PaymentMethod))
                throw new InvalidOperationException("Choose a payment method.");

            var companyId = CompanyId;
            var settlementDate = input.SettlementDate.Date;

            var settlement = new OwnerSettlement
            {
                CompanyId = companyId,
                SettlementNumber = await NextNumberAsync(companyId, settlementDate.Year),
                OwnerName = statement.OwnerName,
                SettlementDate = settlementDate,
                AsOfDate = statement.AsOfDate,
                GrossRent = statement.RentBilled,
                Commission = statement.Commission,
                CommissionVat = statement.CommissionVat,
                OwnerShare = statement.OwnerShare,
                CollectedOwnerShare = statement.OwnerShareCollected,
                PreviouslySettled = statement.PreviouslySettled + statement.PendingSettlements,
                AmountPaid = amount,
                PaymentMethod = input.PaymentMethod,
                PaidFromAccountId = input.PaidFromAccountId,
                ReferenceNumber = string.IsNullOrWhiteSpace(input.ReferenceNumber) ? null : input.ReferenceNumber.Trim(),
                Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedByAppUserId = userId,
                CreatedByName = userName
            };

            // Tag the journal with the property when the owner has only one, so the
            // landlord balance can still be read per property.
            var propertyCodes = statement.Contracts.Select(x => x.PropertyCode).Distinct().ToList();
            int? propertyId = null;
            if (propertyCodes.Count == 1)
            {
                propertyId = await _db.Properties.AsNoTracking()
                    .Where(x => x.Code == propertyCodes[0])
                    .Select(x => (int?)x.Id)
                    .FirstOrDefaultAsync();
            }

            await using var transaction = await _db.Database.BeginTransactionAsync();

            _db.OwnerSettlements.Add(settlement);
            await _db.SaveChangesAsync();

            var journal = await _posting.PostOwnerSettlementAsync(settlement.Id, propertyId);
            await transaction.CommitAsync();

            var warning = await _workflow.PostNewEntryAsync(journal.Id);
            return (settlement.Id, warning);
        }

        public async Task<OwnerSettlement?> GetAsync(int id)
        {
            return await _db.OwnerSettlements.AsNoTracking()
                .Include(x => x.Company)
                .Include(x => x.JournalEntry)
                .Include(x => x.PaidFromAccount)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        private async Task<string> NextNumberAsync(int companyId, int year)
        {
            var prefix = $"OS-{year}-";
            var last = await _db.OwnerSettlements.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.SettlementNumber.StartsWith(prefix))
                .Select(x => x.SettlementNumber)
                .ToListAsync();

            var next = last
                .Select(x => int.TryParse(x[prefix.Length..], out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .Max() + 1;

            return $"{prefix}{next:D4}";
        }

        /// <summary>The annual rent spread over the full months of the contract term.</summary>
        private static decimal AgreementRent(Lease lease)
        {
            var endExclusive = lease.EndDate.Date.AddDays(1);
            var months = (endExclusive.Year - lease.StartDate.Year) * 12 + endExclusive.Month - lease.StartDate.Month;
            if (endExclusive.Day < lease.StartDate.Day)
                months--;

            months = Math.Max(months, 1);
            return Math.Round(lease.AnnualRent / 12m * months, 2);
        }
    }
}
