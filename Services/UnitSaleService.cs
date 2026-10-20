using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Selling units: the sale and purchase agreement, its payment plan, the buyer's payments
    /// and the handover. Revenue is recognised at handover (point in time):
    ///
    ///   Payment before handover   Dr Bank                     Cr 21250 Advances from Unit Buyers (price part)
    ///                                                         Cr 21400 VAT Payable (standard-rated sales)
    ///   Handover                  Dr 21250 advances received  Cr 41200 Revenue from Sale of Properties
    ///                             Dr 11250 unpaid balance     Cr 21400 VAT on the unpaid balance
    ///                             Dr 51200 Cost of Properties Sold    Cr 13200 the unit's cost
    ///   Payment after handover    Dr Bank                     Cr 11250 Receivable from Unit Buyers
    /// </summary>
    public class UnitSaleService
    {
        public const string ReceiptSource = "UnitSaleReceipt";
        public const string HandoverSource = "UnitSaleHandover";

        public static readonly string[] PaymentMethods = { "Bank Transfer", "Cheque", "Cash" };

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;
        private readonly RealEstateVatService _vat;
        private readonly VatDeterminationService _vatNumbers;

        public UnitSaleService(
            ApplicationDbContext db,
            ICurrentCompanyAccessor currentCompany,
            AccountingPostingService posting,
            JournalWorkflowService workflow,
            RealEstateVatService vat,
            VatDeterminationService vatNumbers)
        {
            _db = db;
            _currentCompany = currentCompany;
            _posting = posting;
            _workflow = workflow;
            _vat = vat;
            _vatNumbers = vatNumbers;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        // =====================================================================
        //  READ
        // =====================================================================

        public async Task<List<UnitSaleSummary>> GetSalesAsync()
        {
            var sales = await _db.UnitSales.AsNoTracking()
                .Include(x => x.Unit).ThenInclude(x => x!.Property)
                .Include(x => x.Buyer)
                .Include(x => x.Receipts).ThenInclude(x => x.JournalEntry)
                .OrderByDescending(x => x.ContractDate).ThenByDescending(x => x.Id)
                .ToListAsync();

            return sales.Select(x => new UnitSaleSummary(x, Received(x))).ToList();
        }

        public async Task<UnitSale?> GetAsync(int id)
        {
            return await _db.UnitSales.AsNoTracking()
                .Include(x => x.Unit).ThenInclude(x => x!.Property)
                .Include(x => x.Buyer)
                .Include(x => x.Instalments)
                .Include(x => x.Receipts).ThenInclude(x => x.JournalEntry)
                .Include(x => x.HandoverJournalEntry)
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        /// <summary>What the buyer has paid: posted receipts only.</summary>
        public static decimal Received(UnitSale sale) =>
            sale.Receipts.Where(r => r.JournalEntry?.IsPosted == true).Sum(r => r.Amount);

        /// <summary>The unit's cost in completed properties held for sale (13200), read from the ledger.</summary>
        public async Task<decimal> GetUnitCostAsync(int unitId)
        {
            return await _db.JournalEntryLines.AsNoTracking()
                .Where(x => x.UnitId == unitId && x.Account != null && x.Account.Code == "13200" &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted)
                .SumAsync(x => (decimal?)(x.Debit - x.Credit)) ?? 0m;
        }

        /// <summary>Units that can be sold: active, owned, and not already under a live sale contract.</summary>
        public async Task<List<Unit>> GetSaleableUnitsAsync()
        {
            var taken = await _db.UnitSales.AsNoTracking()
                .Where(x => x.Status != UnitSaleStatuses.Cancelled)
                .Select(x => x.UnitId)
                .ToListAsync();

            return await _db.Units.AsNoTracking()
                .Include(x => x.Property)
                .Where(x => x.IsActive && x.Property != null && x.Property.OwnershipType == "Owned" && !taken.Contains(x.Id))
                .OrderBy(x => x.Property!.Code).ThenBy(x => x.UnitNumber)
                .ToListAsync();
        }

        public async Task<List<Account>> GetReceiptAccountsAsync()
        {
            return await _db.Accounts.AsNoTracking()
                .Where(x => x.AccountType == "Asset" && x.IsPostable && x.IsActive && !x.IsGroup && x.Code.StartsWith("111"))
                .OrderBy(x => x.Code)
                .ToListAsync();
        }

        public Task<RealEstateVatService.VatDecision> PreviewVatAsync(int unitId, DateTime contractDate) =>
            _vat.DetermineAsync(unitId, contractDate);

        // =====================================================================
        //  CONTRACT
        // =====================================================================

        public async Task<UnitSale> CreateAsync(
            int unitId, int buyerId, DateTime contractDate, decimal salePrice,
            List<(string Description, DateTime DueDate, decimal Amount)> plan, string? notes, string? userName)
        {
            var companyId = CompanyId;

            if (salePrice <= 0m)
                throw new InvalidOperationException("Enter the sale price before VAT.");
            if (contractDate.Date > DateTime.Today)
                throw new InvalidOperationException("The contract date cannot be in the future.");
            if ((await GetSaleableUnitsAsync()).All(x => x.Id != unitId))
                throw new InvalidOperationException("That unit cannot be sold: it is not an active owned unit, or it is already under a sale contract.");
            if (!await _db.Tenants.AnyAsync(x => x.Id == buyerId))
                throw new InvalidOperationException("Choose the buyer.");

            var decision = await _vat.DetermineAsync(unitId, contractDate);
            var rate = VatDeterminationService.RateFor(decision.Category);
            var vat = VatDeterminationService.Round(salePrice * rate / 100m);
            var total = salePrice + vat;

            var steps = plan.Where(x => x.Amount != 0m).ToList();
            if (steps.Count == 0)
                steps.Add(("Full price", contractDate.Date, total));
            if (steps.Any(x => x.Amount < 0m))
                throw new InvalidOperationException("Payment plan amounts cannot be negative.");
            if (steps.Sum(x => x.Amount) != total)
                throw new InvalidOperationException(
                    $"The payment plan adds up to {steps.Sum(x => x.Amount):N2} but the price including VAT is {total:N2} " +
                    $"({VatCategories.Describe(decision.Category)}). Adjust the instalments.");

            var unit = await _db.Units.FirstAsync(x => x.Id == unitId);

            var sale = new UnitSale
            {
                CompanyId = companyId,
                ContractNumber = await NextNumberAsync("SPA", companyId, contractDate.Year, _db.UnitSales.Select(x => x.ContractNumber)),
                UnitId = unitId,
                BuyerId = buyerId,
                ContractDate = contractDate.Date,
                SalePrice = salePrice,
                VatCategory = decision.Category,
                VatRate = rate,
                VatAmount = vat,
                TotalPrice = total,
                VatReason = decision.Reason,
                Status = UnitSaleStatuses.Active,
                Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim(),
                CreatedAt = DateTime.UtcNow,
                CreatedByName = userName
            };

            var sequence = 1;
            foreach (var step in steps.OrderBy(x => x.DueDate))
            {
                sale.Instalments.Add(new UnitSaleInstalment
                {
                    Sequence = sequence++,
                    Description = string.IsNullOrWhiteSpace(step.Description) ? $"Instalment {sequence - 1}" : step.Description.Trim(),
                    DueDate = step.DueDate.Date,
                    Amount = step.Amount
                });
            }

            _db.UnitSales.Add(sale);

            // The sale is the unit's first supply if none came before it.
            RealEstateVatService.RecordFirstSupply(unit, sale.ContractDate, "Sale", sale.ContractNumber);
            if (unit.Status == "Vacant")
                unit.Status = "Reserved";

            await _db.SaveChangesAsync();
            return sale;
        }

        /// <summary>Only a contract with nothing paid can be cancelled; a refund is a journal, decided case by case.</summary>
        public async Task CancelAsync(int saleId)
        {
            var sale = await _db.UnitSales.Include(x => x.Receipts).Include(x => x.Unit).FirstOrDefaultAsync(x => x.Id == saleId)
                       ?? throw new InvalidOperationException("The sale contract was not found.");
            if (sale.Status != UnitSaleStatuses.Active)
                throw new InvalidOperationException("Only a contract not yet handed over can be cancelled.");
            if (sale.Receipts.Count > 0)
                throw new InvalidOperationException(
                    "Payments are recorded on this contract. Agree the refund or forfeiture with the buyer and record it with a journal before cancelling.");

            sale.Status = UnitSaleStatuses.Cancelled;
            if (sale.Unit != null)
            {
                if (sale.Unit.Status == "Reserved")
                    sale.Unit.Status = "Vacant";
                if (sale.Unit.FirstSupplyType == "Sale" && sale.Unit.FirstSupplyReference == sale.ContractNumber)
                {
                    sale.Unit.FirstSupplyDate = null;
                    sale.Unit.FirstSupplyType = null;
                    sale.Unit.FirstSupplyReference = null;
                }
            }

            await _db.SaveChangesAsync();
        }

        // =====================================================================
        //  PAYMENTS
        // =====================================================================

        public async Task<UnitSaleReceipt> RecordReceiptAsync(
            int saleId, DateTime receiptDate, decimal amount, string paymentMethod, int accountId, string? reference)
        {
            var companyId = CompanyId;
            var sale = await GetAsync(saleId) ?? throw new InvalidOperationException("The sale contract was not found.");

            if (sale.Status == UnitSaleStatuses.Cancelled)
                throw new InvalidOperationException("The contract is cancelled.");
            if (amount <= 0m)
                throw new InvalidOperationException("Enter the amount received.");
            if (receiptDate.Date > DateTime.Today)
                throw new InvalidOperationException("The receipt cannot be dated in the future.");
            if (receiptDate.Date < sale.ContractDate)
                throw new InvalidOperationException("The receipt cannot be dated before the contract.");
            if (!PaymentMethods.Contains(paymentMethod))
                throw new InvalidOperationException("Choose the payment method.");
            if ((await GetReceiptAccountsAsync()).All(x => x.Id != accountId))
                throw new InvalidOperationException("Choose the bank or cash account the money went into.");

            var outstanding = sale.TotalPrice - sale.Receipts.Sum(x => x.Amount);
            if (amount > outstanding)
                throw new InvalidOperationException($"Only {outstanding:N2} is still due on this contract.");

            var beforeHandover = sale.Status == UnitSaleStatuses.Active;
            if (!beforeHandover && receiptDate.Date < sale.HandoverDate)
                throw new InvalidOperationException("A payment dated before the handover must be recorded before the handover is posted.");

            decimal net = amount, vat = 0m;
            if (beforeHandover && sale.VatRate > 0m)
            {
                // The last payment takes whatever VAT is left, so the parts always add up.
                var vatReceived = sale.Receipts.Where(x => x.IsBeforeHandover).Sum(x => x.VatAmount);
                vat = amount == outstanding
                    ? sale.VatAmount - vatReceived
                    : VatDeterminationService.Round(amount * sale.VatRate / (100m + sale.VatRate));
                net = amount - vat;
            }

            var receipt = new UnitSaleReceipt
            {
                CompanyId = companyId,
                UnitSaleId = sale.Id,
                ReceiptNumber = await NextNumberAsync("SR", companyId, receiptDate.Year, _db.UnitSaleReceipts.Select(x => x.ReceiptNumber)),
                // A payment on a standard-rated sale is a supply for VAT when received: it needs a tax invoice.
                TaxInvoiceNumber = vat > 0m ? await _vatNumbers.GenerateTaxInvoiceNumberAsync(companyId, receiptDate) : null,
                ReceiptDate = receiptDate.Date,
                Amount = amount,
                NetAmount = beforeHandover ? net : 0m,
                VatAmount = vat,
                IsBeforeHandover = beforeHandover,
                PaymentMethod = paymentMethod,
                ReceivedIntoAccountId = accountId,
                Reference = string.IsNullOrWhiteSpace(reference) ? null : reference.Trim(),
                CreatedAt = DateTime.UtcNow
            };
            _db.UnitSaleReceipts.Add(receipt);
            await _db.SaveChangesAsync();

            var accounts = await AccountsAsync("21250", "21400", "11250");
            var lines = new List<JournalEntryLine>
            {
                new() { AccountId = accountId, Debit = amount, Description = $"{sale.ContractNumber} - payment {receipt.ReceiptNumber}", PropertyId = sale.Unit!.PropertyId, UnitId = sale.UnitId }
            };
            if (beforeHandover)
            {
                lines.Add(new JournalEntryLine { AccountId = accounts["21250"], Credit = net, Description = $"Advance from buyer - {sale.ContractNumber}", PropertyId = sale.Unit.PropertyId, UnitId = sale.UnitId });
                if (vat > 0m)
                    lines.Add(new JournalEntryLine { AccountId = accounts["21400"], Credit = vat, Description = $"Output VAT on payment {receipt.TaxInvoiceNumber}", PropertyId = sale.Unit.PropertyId, UnitId = sale.UnitId });
            }
            else
            {
                lines.Add(new JournalEntryLine { AccountId = accounts["11250"], Credit = amount, Description = $"Buyer balance settled - {sale.ContractNumber}", PropertyId = sale.Unit.PropertyId, UnitId = sale.UnitId });
            }

            var journal = await _posting.CreatePreparedJournalAsync(companyId, receipt.ReceiptDate,
                $"Unit sale payment {receipt.ReceiptNumber} - {sale.ContractNumber}", ReceiptSource, receipt.Id, lines,
                receipt.TaxInvoiceNumber ?? receipt.ReceiptNumber, sale.ContractNumber);
            receipt.JournalEntryId = journal.Id;
            await _db.SaveChangesAsync();

            await PostOrUndoAsync(journal.Id, () => _db.UnitSaleReceipts.Remove(receipt));
            return receipt;
        }

        // =====================================================================
        //  HANDOVER
        // =====================================================================

        public async Task HandoverAsync(int saleId, DateTime handoverDate, string? buyerTitleDeed)
        {
            var companyId = CompanyId;
            var sale = await GetAsync(saleId) ?? throw new InvalidOperationException("The sale contract was not found.");

            if (sale.Status != UnitSaleStatuses.Active)
                throw new InvalidOperationException("Only a signed contract not yet handed over can be handed over.");
            if (handoverDate.Date > DateTime.Today)
                throw new InvalidOperationException("The handover cannot be dated in the future.");
            if (handoverDate.Date < sale.ContractDate)
                throw new InvalidOperationException("The handover cannot be before the contract date.");
            if (sale.Receipts.Any(x => x.JournalEntry?.IsPosted != true))
                throw new InvalidOperationException("A payment on this contract is not posted yet. Post it first.");
            if (sale.Receipts.Any(x => x.ReceiptDate > handoverDate.Date))
                throw new InvalidOperationException("A payment is dated after this handover date - hand over on or after the last payment, or correct the date.");

            var cost = await GetUnitCostAsync(sale.UnitId);
            if (cost <= 0m)
                throw new InvalidOperationException(
                    "The unit has no cost in 13200 Completed Properties Held for Sale. Complete its development project and transfer " +
                    "the unit as 'for sale' first - the handover takes that cost to cost of sales.");

            var receivedNet = sale.Receipts.Sum(x => x.NetAmount);
            var receivedVat = sale.Receipts.Sum(x => x.VatAmount);
            var balanceNet = sale.SalePrice - receivedNet;
            var balanceVat = sale.VatAmount - receivedVat;

            var accounts = await AccountsAsync("21250", "11250", "41200", "21400", "51200", "13200");
            var propertyId = sale.Unit!.PropertyId;
            var lines = new List<JournalEntryLine>();
            if (receivedNet > 0m)
                lines.Add(new JournalEntryLine { AccountId = accounts["21250"], Debit = receivedNet, Description = $"Advances applied on handover - {sale.ContractNumber}", PropertyId = propertyId, UnitId = sale.UnitId });
            if (balanceNet + balanceVat > 0m)
                lines.Add(new JournalEntryLine { AccountId = accounts["11250"], Debit = balanceNet + balanceVat, Description = $"Balance due from buyer - {sale.ContractNumber}", PropertyId = propertyId, UnitId = sale.UnitId });
            lines.Add(new JournalEntryLine { AccountId = accounts["41200"], Credit = sale.SalePrice, Description = $"Sale of unit {sale.Unit.UnitNumber} - {sale.ContractNumber}", PropertyId = propertyId, UnitId = sale.UnitId });
            if (balanceVat > 0m)
                lines.Add(new JournalEntryLine { AccountId = accounts["21400"], Credit = balanceVat, Description = $"Output VAT on the balance at handover - {sale.ContractNumber}", PropertyId = propertyId, UnitId = sale.UnitId });
            lines.Add(new JournalEntryLine { AccountId = accounts["51200"], Debit = cost, Description = $"Cost of unit {sale.Unit.UnitNumber} sold", PropertyId = propertyId, UnitId = sale.UnitId });
            lines.Add(new JournalEntryLine { AccountId = accounts["13200"], Credit = cost, Description = $"Unit {sale.Unit.UnitNumber} handed over", PropertyId = propertyId, UnitId = sale.UnitId });

            var journal = await _posting.CreatePreparedJournalAsync(companyId, handoverDate.Date,
                $"Handover of unit {sale.Unit.UnitNumber} - {sale.ContractNumber}", HandoverSource, sale.Id, lines,
                sale.ContractNumber, sale.Buyer?.FullName);

            var warning = await _workflow.PostNewEntryAsync(journal.Id);
            if (warning != null)
            {
                await RemoveDraftAsync(journal.Id);
                throw new InvalidOperationException($"The handover could not be posted: {warning}");
            }

            var tracked = await _db.UnitSales.Include(x => x.Unit).FirstAsync(x => x.Id == saleId);
            tracked.Status = UnitSaleStatuses.HandedOver;
            tracked.HandoverDate = handoverDate.Date;
            tracked.CostOfSales = cost;
            tracked.HandoverNetAmount = balanceNet;
            tracked.HandoverVatAmount = balanceVat;
            tracked.HandoverJournalEntryId = journal.Id;
            tracked.BuyerTitleDeedNumber = string.IsNullOrWhiteSpace(buyerTitleDeed) ? null : buyerTitleDeed.Trim();
            tracked.Unit!.Status = "Sold";
            await _db.SaveChangesAsync();
        }

        // =====================================================================
        //  HELPERS
        // =====================================================================

        private async Task PostOrUndoAsync(int journalId, Action undo)
        {
            var warning = await _workflow.PostNewEntryAsync(journalId);
            if (warning == null)
                return;

            await RemoveDraftAsync(journalId);
            undo();
            await _db.SaveChangesAsync();
            throw new InvalidOperationException($"It could not be posted, so nothing was recorded: {warning}");
        }

        private async Task RemoveDraftAsync(int journalId)
        {
            var draft = await _db.JournalEntries.Include(x => x.Lines).FirstAsync(x => x.Id == journalId);
            foreach (var receipt in _db.UnitSaleReceipts.Local.Where(x => x.JournalEntryId == journalId))
                receipt.JournalEntryId = null;
            await _db.SaveChangesAsync();
            _db.JournalEntryLines.RemoveRange(draft.Lines);
            _db.JournalEntries.Remove(draft);
            await _db.SaveChangesAsync();
        }

        private async Task<Dictionary<string, int>> AccountsAsync(params string[] codes)
        {
            var accounts = await _db.Accounts.AsNoTracking()
                .Where(x => codes.Contains(x.Code))
                .ToDictionaryAsync(x => x.Code, x => x.Id);
            var missing = codes.Where(c => !accounts.ContainsKey(c)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException($"Account(s) {string.Join(", ", missing)} are missing. Restart the application once so they are added.");
            return accounts;
        }

        private static async Task<string> NextNumberAsync(string prefix, int companyId, int year, IQueryable<string> existing)
        {
            var start = $"{prefix}-{year}-";
            var numbers = await existing.Where(x => x.StartsWith(start)).ToListAsync();
            var next = numbers.Select(x => int.TryParse(x[start.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max() + 1;
            return $"{start}{next:D4}";
        }
    }

    public sealed record UnitSaleSummary(UnitSale Sale, decimal Received)
    {
        public decimal Outstanding => Sale.TotalPrice - Received;
    }
}
