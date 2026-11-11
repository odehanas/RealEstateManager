using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The handover of a unit when its contract ends (see <see cref="LeaseHandover"/>):
    /// opening it, the inspection, the Maintenance Manager's charges, and the settlement
    /// of the security deposit by the accounts.
    ///
    /// Nothing reaches the accounts until the settlement. It then does three things, each
    /// through the ordinary posting rules: invoices the charges to the tenant, applies the
    /// deposit to what the tenant owes (a receipt against each invoice, paid from the
    /// deposit instead of the bank), and refunds what is left.
    /// </summary>
    public class LeaseHandoverService
    {
        public const string ChargeType = "Handover charges";

        private readonly ApplicationDbContext _db;
        private readonly AccountingPostingService _posting;
        private readonly JournalWorkflowService _workflow;

        public LeaseHandoverService(ApplicationDbContext db, AccountingPostingService posting, JournalWorkflowService workflow)
        {
            _db = db;
            _posting = posting;
            _workflow = workflow;
        }

        // =====================================================================
        //  THE COMPANY'S CHECKLIST
        // =====================================================================

        /// <summary>The company's checklist; the standard one is copied in the first time it is needed.</summary>
        public async Task<List<HandoverChecklistItem>> ChecklistAsync(int companyId)
        {
            if (!await _db.HandoverChecklistItems.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId))
            {
                var order = 0;
                foreach (var name in HandoverChecklistItem.Standard)
                    _db.HandoverChecklistItems.Add(new HandoverChecklistItem { CompanyId = companyId, Name = name, SortOrder = order += 10 });
                await _db.SaveChangesAsync();
            }

            return await _db.HandoverChecklistItems.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .ToListAsync();
        }

        public async Task<string?> AddChecklistItemAsync(int companyId, string? name)
        {
            var clean = Clean(name);
            if (clean.Length == 0) return "Type what is to be checked.";
            if (clean.Length > HandoverChecklistItem.MaxNameLength) return $"Keep it within {HandoverChecklistItem.MaxNameLength} characters.";

            var existing = await ChecklistAsync(companyId);
            if (existing.Any(x => string.Equals(x.Name, clean, StringComparison.OrdinalIgnoreCase)))
                return "That is already on the checklist.";

            _db.HandoverChecklistItems.Add(new HandoverChecklistItem
            {
                CompanyId = companyId,
                Name = clean,
                SortOrder = (existing.Count == 0 ? 0 : existing.Max(x => x.SortOrder)) + 10
            });
            await _db.SaveChangesAsync();
            return null;
        }

        /// <summary>Renames a checklist line, moves it up or down, or switches it off or on.</summary>
        public async Task<string?> ChangeChecklistItemAsync(int companyId, int id, string? name, string? move, bool toggle)
        {
            var items = await _db.HandoverChecklistItems.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .OrderBy(x => x.SortOrder).ThenBy(x => x.Id)
                .ToListAsync();
            var item = items.FirstOrDefault(x => x.Id == id);
            if (item == null) return "That checklist line was not found.";

            if (toggle)
            {
                item.IsActive = !item.IsActive;
            }
            else if (move is "up" or "down")
            {
                var index = items.IndexOf(item);
                var other = move == "up" ? index - 1 : index + 1;
                if (other >= 0 && other < items.Count)
                {
                    (items[index], items[other]) = (items[other], items[index]);
                    for (var i = 0; i < items.Count; i++)
                        items[i].SortOrder = (i + 1) * 10;
                }
            }
            else
            {
                var clean = Clean(name);
                if (clean.Length == 0) return "Type what is to be checked.";
                if (clean.Length > HandoverChecklistItem.MaxNameLength) return $"Keep it within {HandoverChecklistItem.MaxNameLength} characters.";
                item.Name = clean;
            }

            await _db.SaveChangesAsync();
            return null;
        }

        private static string Clean(string? text) =>
            string.Join(' ', (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // =====================================================================
        //  OPENING A HANDOVER
        // =====================================================================

        public sealed record LeaseOption(int Id, string ContractNumber, string Tenant, string Unit, DateTime EndDate);

        /// <summary>Contracts in force that have no handover under way.</summary>
        public async Task<List<LeaseOption>> LeasesToHandOverAsync(int companyId)
        {
            var busy = await _db.LeaseHandovers.IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Status != HandoverStatuses.Cancelled)
                .Select(x => x.LeaseId).ToListAsync();

            return (await _db.Leases.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.Status == LeaseStatuses.Active && !busy.Contains(x.Id))
                    .OrderBy(x => x.EndDate)
                    .Select(x => new { x.Id, x.ContractNumber, Tenant = x.Tenant!.FullName, Unit = x.Unit!.UnitNumber, x.EndDate })
                    .ToListAsync())
                .Select(x => new LeaseOption(x.Id, x.ContractNumber, x.Tenant, x.Unit, x.EndDate))
                .ToList();
        }

        public async Task<(LeaseHandover? Handover, string? Error)> StartAsync(int companyId, int leaseId, DateTime handoverDate, string? byName)
        {
            var lease = await _db.Leases.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == leaseId && x.CompanyId == companyId);
            if (lease == null) return (null, "Choose the contract.");
            if (lease.Status != LeaseStatuses.Active) return (null, "Only a contract in force is handed over.");
            if (handoverDate.Date < lease.StartDate.Date) return (null, "The handover cannot be before the contract started.");
            if (await _db.LeaseHandovers.IgnoreQueryFilters().AnyAsync(x => x.LeaseId == leaseId && x.Status != HandoverStatuses.Cancelled))
                return (null, "This contract already has a handover.");

            var year = handoverDate.Year;
            var prefix = $"HND-{year}-";
            var used = await _db.LeaseHandovers.IgnoreQueryFilters()
                .CountAsync(x => x.CompanyId == companyId && x.HandoverNumber.StartsWith(prefix));

            var handover = new LeaseHandover
            {
                CompanyId = companyId,
                LeaseId = leaseId,
                HandoverNumber = $"{prefix}{used + 1:D4}",
                HandoverDate = handoverDate.Date,
                Reason = handoverDate.Date < lease.EndDate.Date ? HandoverReasons.EarlyTermination : HandoverReasons.EndOfContract,
                CreatedByName = byName
            };
            foreach (var line in (await ChecklistAsync(companyId)).Where(x => x.IsActive))
                handover.Items.Add(new LeaseHandoverItem { Name = line.Name, SortOrder = line.SortOrder });

            _db.LeaseHandovers.Add(handover);
            await _db.SaveChangesAsync();
            return (handover, null);
        }

        public Task<LeaseHandover?> LoadAsync(int companyId, int id) =>
            _db.LeaseHandovers.IgnoreQueryFilters()
                .Include(x => x.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id))
                .Include(x => x.Charges)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Tenant)
                .Include(x => x.Lease)!.ThenInclude(x => x!.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);

        public async Task<string?> CancelAsync(int companyId, int id)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return "The handover was not found.";
            if (!handover.IsOpen) return "Only a handover still open can be cancelled.";
            if (handover.ChargesInvoiceId.HasValue || handover.DepositApplied > 0m)
                return "The settlement has started in the accounts, so this handover can no longer be cancelled. Finish settling it.";

            handover.Status = HandoverStatuses.Cancelled;
            await _db.SaveChangesAsync();
            return null;
        }

        // =====================================================================
        //  STEP 1: THE INSPECTION
        // =====================================================================

        public sealed class InspectionInput
        {
            public int Id { get; set; }
            public int? KeysReturned { get; set; }
            public string? ElectricityReading { get; set; }
            public string? WaterReading { get; set; }
            public string? InspectionNotes { get; set; }
            public List<ItemInput> Items { get; set; } = new();

            /// <summary>Save and sign the inspection off, rather than save to carry on later.</summary>
            public bool Complete { get; set; }
        }

        public sealed class ItemInput
        {
            public int Id { get; set; }
            public string? Result { get; set; }
            public string? Notes { get; set; }
        }

        public async Task<(bool Ok, string Message)> SaveInspectionAsync(int companyId, InspectionInput input, string? byName)
        {
            var handover = await LoadAsync(companyId, input.Id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen) return (false, "This handover is closed.");
            if (handover.IsMaintenanceSigned) return (false, "The Maintenance Manager has signed, so the inspection can no longer be changed.");
            if (input.KeysReturned is < 0 or > 99) return (false, "Give the number of keys returned.");

            foreach (var item in handover.Items)
            {
                var answer = input.Items.FirstOrDefault(x => x.Id == item.Id);
                if (answer == null) continue;
                item.Result = HandoverItemResults.All.Contains(answer.Result) ? answer.Result! : HandoverItemResults.Pending;
                item.Notes = Trim(answer.Notes, 500);
            }

            handover.KeysReturned = input.KeysReturned;
            handover.ElectricityReading = Trim(input.ElectricityReading, 50);
            handover.WaterReading = Trim(input.WaterReading, 50);
            handover.InspectionNotes = Trim(input.InspectionNotes, 2000);

            if (input.Complete)
            {
                var unchecked_ = handover.Items.Count(x => x.Result == HandoverItemResults.Pending);
                if (unchecked_ > 0)
                {
                    await _db.SaveChangesAsync();
                    return (false, $"Saved, but {unchecked_} checklist line(s) are not checked yet. Answer each one, then complete the inspection.");
                }
                if (handover.Items.Any(x => x.Result == HandoverItemResults.Issue && string.IsNullOrWhiteSpace(x.Notes)))
                {
                    await _db.SaveChangesAsync();
                    return (false, "Saved. Add a note to each line marked as a problem, saying what was found, then complete the inspection.");
                }

                handover.InspectedAt = DateTime.UtcNow;
                handover.InspectedByName = byName;
            }

            await _db.SaveChangesAsync();
            return (true, input.Complete
                ? "Inspection completed. It now goes to the Maintenance Manager."
                : "Inspection saved. Complete it when every line is checked.");
        }

        // =====================================================================
        //  STEP 2: THE MAINTENANCE MANAGER
        // =====================================================================

        public async Task<(bool Ok, string Message)> AddChargeAsync(int companyId, int id, string? description, decimal amount, string? byName)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen || handover.IsMaintenanceSigned) return (false, "The charges are signed off and can no longer be changed.");
            if (!handover.IsInspected) return (false, "Complete the inspection first.");
            if (string.IsNullOrWhiteSpace(description)) return (false, "Say what the charge is for.");
            if (amount <= 0m) return (false, "Give the amount of the charge.");

            handover.Charges.Add(new LeaseHandoverCharge
            {
                Description = Trim(description, 300)!,
                Amount = Math.Round(amount, 2),
                CreatedByName = byName
            });
            await _db.SaveChangesAsync();
            return (true, "Charge added.");
        }

        public async Task<(bool Ok, string Message)> RemoveChargeAsync(int companyId, int id, int chargeId)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen || handover.IsMaintenanceSigned) return (false, "The charges are signed off and can no longer be changed.");

            var charge = handover.Charges.FirstOrDefault(x => x.Id == chargeId);
            if (charge != null)
            {
                _db.LeaseHandoverCharges.Remove(charge);
                await _db.SaveChangesAsync();
            }
            return (true, "Charge removed.");
        }

        public async Task<(bool Ok, string Message)> SignMaintenanceAsync(int companyId, int id, string? notes, string? byName)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen) return (false, "This handover is closed.");
            if (!handover.IsInspected) return (false, "Complete the inspection first.");
            if (handover.IsMaintenanceSigned) return (false, "The Maintenance Manager has already signed.");

            handover.MaintenanceNotes = Trim(notes, 2000);
            handover.MaintenanceSignedAt = DateTime.UtcNow;
            handover.MaintenanceSignedByName = byName;
            await _db.SaveChangesAsync();
            return (true, handover.Charges.Count == 0
                ? "Signed: nothing to charge the tenant. It now goes to the accounts."
                : $"Signed: {handover.Charges.Sum(x => x.Amount):N2} to charge the tenant. It now goes to the accounts.");
        }

        /// <summary>The Maintenance Manager takes the signature back to correct the charges, before the accounts settle.</summary>
        public async Task<(bool Ok, string Message)> ReopenMaintenanceAsync(int companyId, int id)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen) return (false, "This handover is closed.");
            if (handover.ChargesInvoiceId.HasValue) return (false, "The charges have been invoiced, so they can no longer be changed.");

            handover.MaintenanceSignedAt = null;
            handover.MaintenanceSignedByName = null;
            await _db.SaveChangesAsync();
            return (true, "Reopened: the charges can be corrected and signed again.");
        }

        // =====================================================================
        //  STEP 3: WHAT THE ACCOUNTS SEE, AND THE SETTLEMENT
        // =====================================================================

        public sealed record InvoiceLine(int Id, string Number, string What, DateTime Date, DateTime DueDate, decimal Outstanding);
        public sealed record ChequeLine(string Number, DateTime Date, decimal Amount, string Status);

        public sealed class Summary
        {
            public decimal DepositHeld { get; set; }

            /// <summary>Deposits recorded for the contract whose entry is not posted: they are not held yet.</summary>
            public decimal DepositNotPosted { get; set; }

            /// <summary>Posted invoices dated up to the handover that are not fully paid.</summary>
            public List<InvoiceLine> Owed { get; set; } = new();

            /// <summary>Invoices dated up to the handover whose entry is still a draft: not owed in the accounts yet.</summary>
            public List<InvoiceLine> NotPosted { get; set; } = new();

            /// <summary>Invoices dated after the handover: rent for a period the tenant will not be there.</summary>
            public List<InvoiceLine> AfterHandover { get; set; } = new();

            public List<ChequeLine> ChequesInHand { get; set; } = new();
            public int OpenRequests { get; set; }

            public decimal ChargesNet { get; set; }
            public decimal ChargesVat { get; set; }
            public decimal ChargesTotal => ChargesNet + ChargesVat;

            /// <summary>True once the charges are an invoice; they are then among <see cref="Owed"/>.</summary>
            public bool ChargesInvoiced { get; set; }

            public decimal OwedTotal => Owed.Sum(x => x.Outstanding) + (ChargesInvoiced ? 0m : ChargesTotal);
            public decimal ToApply => Math.Min(DepositHeld, OwedTotal);
            public decimal ToRefund => DepositHeld - ToApply;
            public decimal StillDue => OwedTotal - ToApply;
        }

        public async Task<Summary> SummaryAsync(LeaseHandover handover)
        {
            var lease = handover.Lease!;
            var date = handover.HandoverDate.Date;
            var summary = new Summary();

            var deposits = await _db.SecurityDeposits.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.LeaseId == lease.Id && x.Status == "Held")
                .Select(x => new { x.Amount, Posted = x.JournalEntry != null && x.JournalEntry.IsPosted })
                .ToListAsync();
            summary.DepositHeld = deposits.Where(x => x.Posted).Sum(x => x.Amount);
            summary.DepositNotPosted = deposits.Where(x => !x.Posted).Sum(x => x.Amount);

            var invoices = await _db.CustomerInvoices.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.LeaseId == lease.Id && x.Status != "Cancelled" && x.TotalAmount > x.AmountPaid)
                .OrderBy(x => x.DueDate).ThenBy(x => x.Id)
                .Select(x => new
                {
                    x.Id, x.InvoiceNumber, x.ChargeType, x.InvoiceDate, x.DueDate,
                    Outstanding = x.TotalAmount - x.AmountPaid,
                    Posted = x.JournalEntry != null && x.JournalEntry.IsPosted
                })
                .ToListAsync();
            foreach (var invoice in invoices)
            {
                var line = new InvoiceLine(invoice.Id, invoice.InvoiceNumber, invoice.ChargeType ?? "Rent", invoice.InvoiceDate, invoice.DueDate, invoice.Outstanding);
                // The charges invoice is dated on the day it is raised, which may be after the handover day.
                if (invoice.InvoiceDate.Date > date && invoice.Id != handover.ChargesInvoiceId)
                    summary.AfterHandover.Add(line);
                else if (invoice.Posted)
                    summary.Owed.Add(line);
                else
                    summary.NotPosted.Add(line);
            }

            summary.ChequesInHand = (await _db.PdcReceipts.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.LeaseId == lease.Id && (x.Status == PdcStatuses.Received || x.Status == PdcStatuses.Deposited))
                    .OrderBy(x => x.ChequeDate)
                    .Select(x => new { x.ChequeNumber, x.ChequeDate, x.Amount, x.Status })
                    .ToListAsync())
                .Select(x => new ChequeLine(x.ChequeNumber, x.ChequeDate, x.Amount, x.Status))
                .ToList();

            summary.OpenRequests = await _db.TenantServiceRequests.IgnoreQueryFilters()
                .CountAsync(x => x.LeaseId == lease.Id &&
                                 x.Status != ServiceRequestStatuses.Closed && x.Status != ServiceRequestStatuses.Cancelled &&
                                 x.Status != ServiceRequestStatuses.Rejected && x.Status != ServiceRequestStatuses.Resolved);

            summary.ChargesInvoiced = handover.ChargesInvoiceId.HasValue;
            if (handover.Charges.Count > 0)
            {
                var company = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == handover.CompanyId);
                var price = PdcService.PriceCharge(company, handover.Charges.Sum(x => x.Amount));
                summary.ChargesNet = price.Net;
                summary.ChargesVat = price.Vat;
            }

            return summary;
        }

        /// <summary>
        /// Settles the handover and closes the contract. Safe to press again after a step
        /// was held up (an entry left as a draft): it carries on from where it stopped.
        /// </summary>
        public async Task<(bool Ok, string Message)> SettleAsync(int companyId, int id, string? notes, string? byName)
        {
            var handover = await LoadAsync(companyId, id);
            if (handover == null) return (false, "The handover was not found.");
            if (!handover.IsOpen) return (false, "This handover is already closed.");
            if (!handover.IsInspected || !handover.IsMaintenanceSigned)
                return (false, "The inspection and the Maintenance Manager's sign-off come first.");

            var lease = handover.Lease!;
            var today = DateTime.Today;
            handover.AccountsNotes = Trim(notes, 2000);

            // The deposit as it stood before any of it was used, kept for the report.
            if (handover.DepositHeld == 0m && handover.DepositApplied == 0m)
            {
                handover.DepositHeld = (await SummaryAsync(handover)).DepositHeld;
                await _db.SaveChangesAsync();
            }

            // 1. The charges become an invoice the tenant owes.
            if (handover.Charges.Count > 0 && !handover.ChargesInvoiceId.HasValue)
            {
                var company = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == companyId);
                var settings = await _db.CompanyAccountSettings.IgnoreQueryFilters().AsNoTracking().FirstOrDefaultAsync(x => x.CompanyId == companyId);
                var price = PdcService.PriceCharge(company, handover.Charges.Sum(x => x.Amount));
                var prefix = $"HNC-{today:yyyy}-";
                var used = await _db.CustomerInvoices.IgnoreQueryFilters()
                    .CountAsync(x => x.CompanyId == companyId && x.InvoiceNumber.StartsWith(prefix));

                var invoice = new CustomerInvoice
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    InvoiceNumber = $"{prefix}{used + 1:D5}",
                    InvoiceDate = today,
                    DueDate = today,
                    DateOfSupply = today,
                    ChargeType = ChargeType,
                    IncomeAccountId = settings?.OtherIncomeAccountId,
                    VatCategory = price.Category,
                    VatRate = VatDeterminationService.RateFor(price.Category),
                    Subtotal = price.Net,
                    VatAmount = price.Vat,
                    TotalAmount = price.Total,
                    Status = "Draft"
                };
                _db.CustomerInvoices.Add(invoice);
                await _db.SaveChangesAsync();
                handover.ChargesInvoiceId = invoice.Id;
                await _db.SaveChangesAsync();

                var journal = await _posting.PostCustomerInvoiceAsync(invoice.Id);
                if (await _workflow.PostNewEntryAsync(journal.Id) is { } held)
                    return (false, $"Invoice {invoice.InvoiceNumber} for the charges was raised but its entry is a draft: {held} Post it, then settle again.");
            }

            // 2. The deposit pays what the tenant owes, oldest first.
            handover = (await LoadAsync(companyId, id))!;
            var summary = await SummaryAsync(handover);
            if (summary.NotPosted.Any(x => x.Id == handover.ChargesInvoiceId))
                return (false, "The invoice for the charges is not posted yet. Post it from Journal Inquiry, then settle again.");

            var left = handover.DepositHeld - handover.DepositApplied;
            foreach (var owed in summary.Owed)
            {
                if (left <= 0m) break;
                var amount = Math.Min(left, owed.Outstanding);

                var receipt = new CustomerReceipt
                {
                    CompanyId = companyId,
                    CustomerInvoiceId = owed.Id,
                    ReceiptNumber = $"{handover.HandoverNumber}-D{await _db.CustomerReceipts.IgnoreQueryFilters().CountAsync(x => x.ReceiptNumber.StartsWith(handover.HandoverNumber + "-D")) + 1:D2}",
                    ReceiptDate = today,
                    Amount = amount,
                    PaymentMethod = ReceiptMethods.SecurityDeposit
                };
                _db.CustomerReceipts.Add(receipt);
                await _db.SaveChangesAsync();

                var journal = await _posting.PostCustomerReceiptAsync(receipt.Id);
                handover.DepositApplied += amount;
                await _db.SaveChangesAsync();
                left -= amount;

                if (await _workflow.PostNewEntryAsync(journal.Id) is { } held)
                    return (false, $"The deposit was applied to invoice {owed.Number} but the entry is a draft: {held} Post it, then settle again.");
            }

            // 3. What is left of the deposit goes back to the tenant.
            if (left > 0m && !handover.RefundJournalEntryId.HasValue)
            {
                var journal = await _posting.PostHandoverRefundAsync(handover.Id, left, today);
                handover.RefundJournalEntryId = journal.Id;
                handover.DepositRefunded = left;
                await _db.SaveChangesAsync();

                if (await _workflow.PostNewEntryAsync(journal.Id) is { } held)
                    return (false, $"The refund of {left:N2} was prepared but its entry is a draft: {held} Post it, then settle again.");
            }

            // 4. The deposit is no longer held, the contract is over and the unit is free.
            var deposits = await _db.SecurityDeposits.IgnoreQueryFilters()
                .Where(x => x.LeaseId == lease.Id && x.Status == "Held" && x.JournalEntry != null && x.JournalEntry.IsPosted)
                .ToListAsync();
            foreach (var deposit in deposits)
                deposit.Status = handover.DepositRefunded > 0m ? "Refunded" : "Applied";

            var after = await SummaryAsync(handover);
            handover.BalanceStillDue = after.Owed.Sum(x => x.Outstanding);
            handover.Status = HandoverStatuses.Settled;
            handover.SettledAt = DateTime.UtcNow;
            handover.SettledByName = byName;

            lease.Status = handover.Reason == HandoverReasons.EarlyTermination ? LeaseStatuses.Terminated : LeaseStatuses.Expired;
            if (lease.Unit != null &&
                !await _db.Leases.IgnoreQueryFilters().AnyAsync(x => x.Id != lease.Id && x.UnitId == lease.UnitId &&
                                                                     (x.Status == LeaseStatuses.Active || x.Status == LeaseStatuses.PendingApproval)))
                lease.Unit.Status = "Vacant";

            await _db.SaveChangesAsync();

            return (true, $"Handover {handover.HandoverNumber} settled. Deposit held {handover.DepositHeld:N2}: " +
                          $"{handover.DepositApplied:N2} applied to what was owed, {handover.DepositRefunded:N2} to refund to the tenant" +
                          (handover.BalanceStillDue > 0m ? $", and {handover.BalanceStillDue:N2} still owed by the tenant." : ".") +
                          " The contract is closed and the unit is vacant.");
        }

        private static string? Trim(string? text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var clean = text.Trim();
            return clean.Length > max ? clean[..max] : clean;
        }
    }
}
