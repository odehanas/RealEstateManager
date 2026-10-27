using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>
    /// Day-to-day operations prepared by the assistant: petty cash vouchers, vendor
    /// payments, lease renewals, posting rent invoices, credit notes and new properties.
    /// Each runs the screen's own checks before the card is shown and again on Confirm,
    /// through the same service the screen uses. Approvals stay with people.
    /// </summary>
    public static class AssistantOperationTools
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        public static IEnumerable<AssistantTool> All()
        {
            // ---------------------------------------------------------------- petty cash voucher
            yield return new AssistantTool
            {
                Name = "propose_petty_cash_voucher",
                Description = "Prepare a petty cash expense voucher paid out of a petty cash float (Dr expense, Cr petty cash). Ask for the amount, " +
                              "what it was for, the expense account, who was paid and the receipt number. Posted when the user confirms.",
                Screen = ("PettyCash", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("amount", Schema.Num("Amount paid"), true),
                    ("description", Schema.Str("What it was for"), true),
                    ("expense_account", Schema.Str("Expense account code or name"), true),
                    ("date", Schema.Date("Voucher date, default today"), false),
                    ("paid_to", Schema.Str("Who was paid"), false),
                    ("reference", Schema.Str("Receipt or voucher number"), false),
                    ("float", Schema.Str("Petty cash float account code or name, when there is more than one"), false),
                    ("property", Schema.Str("Property code, when the expense is for a property"), false),
                    ("unit", Schema.Str("Unit number, when it is for a unit"), false)),
                Propose = async (ctx, args) =>
                {
                    var service = ctx.Get<PettyCashService>();
                    var floats = await service.GetFloatAccountsAsync();
                    if (floats.Count == 0)
                        return AssistantProposal.Refused("No account is set up as a petty cash float. Flag one in the chart of accounts first.");
                    var floatText = Args.Str(args, "float");
                    var matches = floatText == null ? floats
                        : floats.Where(x => x.Code == floatText || x.Name.Contains(floatText, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (matches.Count != 1)
                        return AssistantProposal.Refused(matches.Count == 0
                            ? $"No petty cash float matches '{floatText}'. Floats: {string.Join("; ", floats.Select(f => f.Code + " " + f.Name))}."
                            : $"Which petty cash float? {string.Join("; ", matches.Select(f => f.Code + " " + f.Name))}.");
                    var pettyCash = matches[0];

                    var (expense, accountError) = await AssistantReadTools.ResolveAccountAsync(ctx.Db, Args.Required(args, "expense_account"));
                    if (expense == null) return AssistantProposal.Refused(accountError!);
                    if (expense.AccountType != "Expense" || !expense.IsPostable || expense.IsGroup || !expense.IsActive || expense.IsPettyCashAccount)
                        return AssistantProposal.Refused($"{expense.Code} {expense.Name} is not a postable expense account.");

                    var amount = Args.Dec(args, "amount") ?? 0m;
                    if (amount <= 0m) return AssistantProposal.Refused("Amount must be greater than zero.");
                    var date = Args.Date(args, "date") ?? DateTime.Today;
                    var closed = await AssistantWriteTools.ClosedPeriodMessageAsync(ctx.Db, date, "A petty cash voucher");
                    if (closed != null) return AssistantProposal.Refused(closed);

                    var (propertyId, unitId, placeError, place) = await PlaceAsync(ctx, Args.Str(args, "property"), Args.Str(args, "unit"));
                    if (placeError != null) return AssistantProposal.Refused(placeError);

                    var balance = (await service.GetDashboardAsync(ctx.CompanyId, date, date, pettyCash.Id)).Floats
                        .Where(x => x.AccountId == pettyCash.Id).Select(x => x.ClosingBalance).FirstOrDefault();
                    if (amount > balance)
                        return AssistantProposal.Refused($"The float {pettyCash.Code} holds {balance:N2} on {date:dd/MM/yyyy}; a voucher of {amount:N2} " +
                                                         "would take it below zero. Record a replenishment first.");

                    var model = new PettyCashExpenseViewModel
                    {
                        PettyCashAccountId = pettyCash.Id,
                        ExpenseAccountId = expense.Id,
                        VoucherDate = date,
                        Description = Args.Required(args, "description"),
                        PaidTo = Args.Str(args, "paid_to"),
                        ReferenceNumber = Args.Str(args, "reference"),
                        Amount = amount,
                        PropertyId = propertyId,
                        UnitId = unitId
                    };

                    var warnings = new List<string>();
                    if (model.ReferenceNumber == null) warnings.Add("No receipt number is recorded.");
                    if (balance - amount < balance * 0.2m) warnings.Add($"The float will be down to {balance - amount:N2}; consider a replenishment.");

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Petty cash voucher of {amount:N2} {ctx.Currency} on {date:dd/MM/yyyy} from {pettyCash.Code} {pettyCash.Name}: " +
                                  $"\"{model.Description}\"" + (model.PaidTo != null ? $", paid to {model.PaidTo}" : string.Empty) +
                                  (model.ReferenceNumber != null ? $", receipt {model.ReferenceNumber}" : string.Empty) +
                                  $", charged to {expense.Code} {expense.Name}{place}. It is posted to the accounts.",
                        Warnings = warnings,
                        Normalized = model
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var model = args.Deserialize<PettyCashExpenseViewModel>(Json)!;
                    try
                    {
                        var journalId = await ctx.Get<PettyCashService>().PostExpenseAsync(model);
                        var notPosted = await ctx.Get<JournalWorkflowService>().PostNewEntryAsync(journalId);
                        var number = await AssistantWriteTools.JournalNumberAsync(ctx.Db, journalId);
                        return new AssistantActionResult
                        {
                            Ok = true, Reference = number, JournalEntryNumber = number, Link = "/PettyCash",
                            Message = notPosted == null
                                ? $"Voucher posted: {model.Description} for {model.Amount:N2}."
                                : notPosted.StartsWith(JournalWorkflowService.AwaitingApprovalPrefix)
                                    ? $"Voucher recorded for {model.Amount:N2}. {notPosted}"
                                    : $"Voucher saved as a draft but not posted: {notPosted}"
                        };
                    }
                    catch (Exception ex)
                    {
                        return AssistantActionResult.Failed(ex.Message);
                    }
                }
            };

            // ---------------------------------------------------------------- vendor payment
            yield return new AssistantTool
            {
                Name = "propose_vendor_payment",
                Description = "Prepare paying a vendor's open posted bills (in full or in part), or an advance before a bill arrives. Look up the open bills " +
                              "with the vendor's name first if the user does not name them. Payment approval limits still apply on Confirm: a payment " +
                              "above the user's limit is sent for release by someone else.",
                Screen = ("VendorPayments", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("vendor", Schema.Str("Vendor code or name"), true),
                    ("bills", Schema.Array("Bills to pay; leave out to pay every open bill in full",
                        Schema.Object(
                            ("bill_number", Schema.Str("Our bill number or the supplier's invoice number"), true),
                            ("amount", Schema.Num("Amount to pay; default the whole open amount"), false))), false),
                    ("advance_amount", Schema.Num("For an advance before any bill: the amount. Leave out for a payment of bills"), false),
                    ("date", Schema.Date("Payment date, default today"), false),
                    ("method", Schema.Enum("How it is paid", "Bank", "Transfer", "Cheque", "Cash"), false),
                    ("paid_from", Schema.Str("Cash or bank account code or name; default the company's usual one"), false),
                    ("reference", Schema.Str("Cheque or transfer reference"), false)),
                Propose = async (ctx, args) =>
                {
                    var (vendor, vendorError) = await AssistantReadTools.ResolveVendorAsync(ctx.Db, Args.Required(args, "vendor"));
                    if (vendor == null) return AssistantProposal.Refused(vendorError!);
                    var clearing = ctx.Get<VendorClearingService>();
                    var date = Args.Date(args, "date") ?? DateTime.Today;
                    var closed = await AssistantWriteTools.ClosedPeriodMessageAsync(ctx.Db, date, "A vendor payment");
                    if (closed != null) return AssistantProposal.Refused(closed);

                    int? paidFromId = null;
                    var paidFromText = Args.Str(args, "paid_from");
                    string paidFromLabel = "the company's default cash/bank account";
                    if (paidFromText != null)
                    {
                        var (account, accountError) = await AssistantReadTools.ResolveAccountAsync(ctx.Db, paidFromText);
                        if (account == null) return AssistantProposal.Refused(accountError!);
                        paidFromId = account.Id;
                        paidFromLabel = $"{account.Code} {account.Name}";
                    }

                    var request = new VendorClearingService.PaymentRequest
                    {
                        VendorId = vendor.Id,
                        PaymentDate = date,
                        PaymentMethod = Args.Str(args, "method") ?? "Bank",
                        PaidFromAccountId = paidFromId,
                        Reference = Args.Str(args, "reference")
                    };

                    var advance = Args.Dec(args, "advance_amount");
                    var described = new List<string>();
                    if (advance.HasValue)
                    {
                        request.IsAdvance = true;
                        request.AdvanceAmount = advance.Value;
                        described.Add($"advance of {advance.Value:N2}, held on Advances to Suppliers until applied to bills");
                    }
                    else
                    {
                        var open = await clearing.GetOpenItemsAsync(vendor.Id);
                        if (open.Count == 0)
                            return AssistantProposal.Refused($"{vendor.Name} has no open posted bills. To pay ahead of a bill, give advance_amount.");

                        if (args.TryGetProperty("bills", out var bills) && bills.ValueKind == JsonValueKind.Array && bills.GetArrayLength() > 0)
                        {
                            foreach (var bill in bills.EnumerateArray())
                            {
                                var number = Args.Required(bill, "bill_number");
                                var item = open.FirstOrDefault(x => string.Equals(x.BillNumber, number, StringComparison.OrdinalIgnoreCase) ||
                                                                    string.Equals(x.VendorReference, number, StringComparison.OrdinalIgnoreCase));
                                if (item == null)
                                    return AssistantProposal.Refused($"Bill {number} is not an open posted bill of {vendor.Name}. Open bills: " +
                                                                     string.Join("; ", open.Select(o => $"{o.BillNumber} ({o.VendorReference}) {o.Open:N2}")) + ".");
                                var amount = Args.Dec(bill, "amount") ?? item.Open;
                                request.Allocations.Add(new VendorClearingService.Allocation { BillId = item.BillId, Amount = amount });
                                described.Add($"{item.BillNumber}" + (item.VendorReference != null ? $" (their {item.VendorReference})" : string.Empty) +
                                              $": {amount:N2}" + (amount < item.Open ? $" of {item.Open:N2} open" : " in full"));
                            }
                        }
                        else
                        {
                            foreach (var item in open.Where(x => x.Open > 0m))
                            {
                                request.Allocations.Add(new VendorClearingService.Allocation { BillId = item.BillId, Amount = item.Open });
                                described.Add($"{item.BillNumber}" + (item.VendorReference != null ? $" (their {item.VendorReference})" : string.Empty) + $": {item.Open:N2} in full");
                            }
                        }
                    }

                    var error = await clearing.ValidateAsync(ctx.CompanyId, request);
                    if (error != null) return AssistantProposal.Refused(error);

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Pay {vendor.Code} {vendor.Name} {request.Total:N2} {ctx.Currency} by {request.PaymentMethod} on {date:dd/MM/yyyy} " +
                                  $"from {paidFromLabel}" + (request.Reference != null ? $", reference {request.Reference}" : string.Empty) +
                                  $": {string.Join("; ", described)}. It is posted, or sent for release if the company's payment approval requires it.",
                        Normalized = request
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var request = args.Deserialize<VendorClearingService.PaymentRequest>(Json)!;
                    var clearing = ctx.Get<VendorClearingService>();
                    var error = await clearing.ValidateAsync(ctx.CompanyId, request);
                    if (error != null) return AssistantActionResult.Failed(error);
                    try
                    {
                        var result = await clearing.PayAsync(ctx.CompanyId, request, ctx.UserName);
                        return new AssistantActionResult
                        {
                            Ok = result.Ok,
                            Reference = result.Payment?.PaymentNumber,
                            Link = result.Payment != null ? $"/VendorPayments/Details/{result.Payment.Id}" : "/VendorPayments",
                            Message = result.NotPostedReason == null ? result.Message + " Posted to the accounts." : $"{result.Message} {result.NotPostedReason}"
                        };
                    }
                    catch (Exception ex)
                    {
                        return AssistantActionResult.Failed($"The payment could not be recorded, so nothing was saved. {ex.Message}");
                    }
                }
            };

            // ---------------------------------------------------------------- lease renewal
            yield return new AssistantTool
            {
                Name = "propose_renew_lease",
                Description = "Prepare the renewal of a contract: a new DRAFT contract for the same tenant and unit starting the day after the current one ends. " +
                              "Ask for the new annual rent and the number of cheques if they change. Then offer propose_submit_lease. The current contract is not changed.",
                Screen = ("Leases", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("contract", Schema.Str("The current contract number"), true),
                    ("annual_rent", Schema.Num("New annual rent; default the current rent"), false),
                    ("months", Schema.Int("Length of the renewal in months, default 12"), false),
                    ("billing_frequency_months", Schema.Int("Months per instalment: 1, 2, 3, 4, 6 or 12; default the current contract's"), false),
                    ("security_deposit", Schema.Num("Additional deposit to collect, default 0 (the current deposit stays held)"), false),
                    ("notes", Schema.Str("Notes"), false)),
                Propose = async (ctx, args) =>
                {
                    var number = Args.Required(args, "contract");
                    var lease = await ctx.Db.Leases.AsNoTracking().Include(x => x.Tenant).Include(x => x.Unit).ThenInclude(x => x!.Property)
                        .FirstOrDefaultAsync(x => x.ContractNumber == number);
                    if (lease == null) return AssistantProposal.Refused($"No contract {number}.");
                    if (lease.Status is not (LeaseStatuses.Active or LeaseStatuses.Expired))
                        return AssistantProposal.Refused($"{number} is {lease.Status}; only an active or expired contract is renewed.");

                    var already = await ctx.Db.Leases.AsNoTracking()
                        .Where(x => x.UnitId == lease.UnitId && x.Id != lease.Id && x.StartDate > lease.StartDate &&
                                    x.Status != LeaseStatuses.Terminated)
                        .Select(x => x.ContractNumber + " (" + x.Status + ")").FirstOrDefaultAsync();
                    if (already != null) return AssistantProposal.Refused($"The unit already has a later contract, {already}.");

                    var months = Math.Clamp(Args.Int(args, "months") ?? 12, 1, 120);
                    var start = lease.EndDate.Date.AddDays(1);
                    var model = new LeaseCreateViewModel
                    {
                        TenantId = lease.TenantId,
                        UnitId = lease.UnitId,
                        StartDate = start,
                        EndDate = start.AddMonths(months).AddDays(-1),
                        AnnualRent = Args.Dec(args, "annual_rent") ?? lease.AnnualRent,
                        BillingFrequencyMonths = Args.Int(args, "billing_frequency_months") ?? lease.BillingFrequencyMonths,
                        SecurityDepositAmount = Args.Dec(args, "security_deposit") ?? 0m,
                        Notes = (Args.Str(args, "notes") is { } notes ? notes + " " : string.Empty) + $"Renewal of contract {lease.ContractNumber}."
                    };
                    if (model.AnnualRent <= 0m) return AssistantProposal.Refused("Give the new annual rent.");

                    var service = ctx.Get<LeaseCreationService>();
                    var errors = await service.ValidateAsync(ctx.CompanyId, model);
                    if (errors.Count > 0) return AssistantProposal.Refused(string.Join(" ", errors.Select(e => e.Message)));

                    var change = lease.AnnualRent == 0m ? 0m : Math.Round((model.AnnualRent - lease.AnnualRent) / lease.AnnualRent * 100m, 1);
                    var instalments = 12 / model.BillingFrequencyMonths;
                    var warnings = new List<string>();
                    if (change > 0m) warnings.Add($"Rent rises {change}% from {lease.AnnualRent:N2}; check it against the emirate's rent increase rules and the notice given to the tenant.");
                    if (lease.EndDate.Date < DateTime.Today) warnings.Add($"The current contract ended on {lease.EndDate:dd/MM/yyyy}; the renewal starts the day after.");

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Renew {lease.ContractNumber} for {lease.Tenant?.FullName}, unit {lease.Unit?.Property?.Code}/{lease.Unit?.UnitNumber}: " +
                                  $"new DRAFT contract {model.StartDate:dd/MM/yyyy} to {model.EndDate:dd/MM/yyyy}, rent {model.AnnualRent:N2} {ctx.Currency} a year " +
                                  $"({(change == 0m ? "unchanged" : (change > 0 ? "+" : string.Empty) + change + "%")}) in {instalments} instalment(s), " +
                                  $"additional deposit {model.SecurityDepositAmount:N2}. The current contract is left as it is.",
                        Warnings = warnings,
                        Normalized = model
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var model = args.Deserialize<LeaseCreateViewModel>(Json)!;
                    var service = ctx.Get<LeaseCreationService>();
                    var errors = await service.ValidateAsync(ctx.CompanyId, model);
                    if (errors.Count > 0) return AssistantActionResult.Failed(string.Join(" ", errors.Select(e => e.Message)));
                    var lease = await service.CreateDraftAsync(ctx.CompanyId, ctx.UserId, model);
                    var needsApproval = await ctx.Get<LeaseWorkflowService>().ApprovalRequiredAsync();
                    return new AssistantActionResult
                    {
                        Ok = true, Reference = lease.ContractNumber, Link = $"/Leases/Edit/{lease.Id}",
                        Message = $"Renewal contract {lease.ContractNumber} saved as a draft. Next: " +
                                  (needsApproval ? "send it for approval." : "activate it to raise its invoices.")
                    };
                }
            };

            // ---------------------------------------------------------------- post rent invoices
            yield return new AssistantTool
            {
                Name = "propose_post_customer_invoices",
                Description = "Prepare posting DRAFT rent invoices into the accounts: one invoice by number, or every draft invoice of a contract dated up to a date. " +
                              "Rent invoices are raised by the contract's schedule; there are no free-standing customer invoices in this system.",
                Screen = ("CustomerInvoices", "Post"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("invoice_number", Schema.Str("One invoice number"), false),
                    ("contract", Schema.Str("Contract number, to post its draft invoices"), false),
                    ("up_to", Schema.Date("With a contract: post drafts dated up to this date, default today"), false)),
                Propose = async (ctx, args) =>
                {
                    var invoiceNumber = Args.Str(args, "invoice_number");
                    var contract = Args.Str(args, "contract");
                    if (invoiceNumber == null && contract == null) return AssistantProposal.Refused("Give an invoice number or a contract number.");
                    var upTo = Args.Date(args, "up_to") ?? DateTime.Today;

                    var query = ctx.Db.CustomerInvoices.AsNoTracking().Include(x => x.Lease).ThenInclude(x => x!.Tenant)
                        .Where(x => x.JournalEntryId == null || !x.JournalEntry!.IsPosted);
                    query = invoiceNumber != null
                        ? query.Where(x => x.InvoiceNumber == invoiceNumber)
                        : query.Where(x => x.Lease!.ContractNumber == contract && x.InvoiceDate <= upTo);
                    var invoices = await query.OrderBy(x => x.InvoiceDate).Take(60).ToListAsync();
                    if (invoices.Count == 0)
                        return AssistantProposal.Refused(invoiceNumber != null
                            ? $"No draft invoice {invoiceNumber} (it may be posted already)."
                            : $"Contract {contract} has no draft invoices dated up to {upTo:dd/MM/yyyy}.");

                    var lease = invoices[0].Lease;
                    if (lease != null && lease.Status != LeaseStatuses.Active)
                        return AssistantProposal.Refused($"Contract {lease.ContractNumber} is {lease.Status}; its invoices are posted once it is active.");
                    foreach (var date in invoices.Select(x => x.InvoiceDate.Date).Distinct())
                    {
                        var closed = await AssistantWriteTools.ClosedPeriodMessageAsync(ctx.Db, date, "An invoice");
                        if (closed != null) return AssistantProposal.Refused(closed);
                    }

                    var warnings = new List<string>();
                    if (invoices.Any(x => x.InvoiceDate.Date > DateTime.Today))
                        warnings.Add("Some invoices are dated in the future; they are posted with their own date.");

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Post {invoices.Count} draft invoice(s) for {lease?.Tenant?.FullName}, contract {lease?.ContractNumber}, " +
                                  $"total {invoices.Sum(x => x.TotalAmount):N2} {ctx.Currency}: " +
                                  string.Join("; ", invoices.Take(12).Select(x => $"{x.InvoiceNumber} {x.InvoiceDate:dd/MM/yyyy} {x.TotalAmount:N2}")) +
                                  (invoices.Count > 12 ? $"; and {invoices.Count - 12} more" : string.Empty) + ". They go into the accounts and the tenant's balance.",
                        Warnings = warnings,
                        Normalized = invoices.Select(x => x.Id).ToList()
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var ids = args.Deserialize<List<int>>(Json)!;
                    var posting = ctx.Get<AccountingPostingService>();
                    var workflow = ctx.Get<JournalWorkflowService>();
                    var done = new List<string>();
                    foreach (var id in ids)
                    {
                        var number = await ctx.Db.CustomerInvoices.Where(x => x.Id == id).Select(x => x.InvoiceNumber).FirstOrDefaultAsync();
                        try
                        {
                            var journal = await posting.PostCustomerInvoiceAsync(id);
                            var result = await workflow.PostAsync(journal.Id);
                            if (!result.Success)
                                return AssistantActionResult.Failed((done.Count > 0 ? $"Posted: {string.Join(", ", done)}. " : string.Empty) + $"{number}: {result.Message}");
                            done.Add(number ?? id.ToString());
                        }
                        catch (Exception ex)
                        {
                            return AssistantActionResult.Failed((done.Count > 0 ? $"Posted: {string.Join(", ", done)}. " : string.Empty) + $"{number}: {ex.Message}");
                        }
                    }

                    return new AssistantActionResult
                    {
                        Ok = true, Reference = string.Join(", ", done), Link = "/CustomerInvoices",
                        Message = $"{done.Count} invoice(s) posted: {string.Join(", ", done)}."
                    };
                }
            };

            // ---------------------------------------------------------------- credit note
            yield return new AssistantTool
            {
                Name = "propose_credit_note",
                Description = "Prepare a DRAFT credit note against a posted rent invoice that still has an amount outstanding (a discount, rent-free period, " +
                              "early termination or a tax error). Amounts already paid are refunded, not credited. Post it from Credit Notes after review.",
                Screen = ("CustomerCreditNotes", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("invoice_number", Schema.Str("The invoice to credit"), true),
                    ("reason", Schema.Str("Why, in words the tenant will read"), true),
                    ("reason_code", Schema.Enum("Kind of reason", "SupplyCancelled", "ConsiderationAltered", "ChargedInError"), false),
                    ("net_amount", Schema.Num("Amount before VAT to credit; leave out to credit everything outstanding"), false),
                    ("date", Schema.Date("Credit note date, default today"), false)),
                Propose = async (ctx, args) =>
                {
                    var number = Args.Required(args, "invoice_number");
                    var invoiceId = await ctx.Db.CustomerInvoices.Where(x => x.InvoiceNumber == number).Select(x => (int?)x.Id).FirstOrDefaultAsync();
                    if (invoiceId == null) return AssistantProposal.Refused($"No invoice {number}.");
                    var model = await ctx.Get<CreditNoteService>().BuildNewAsync(invoiceId.Value);
                    if (model == null) return AssistantProposal.Refused($"Invoice {number} cannot be credited.");
                    var posted = await ctx.Db.CustomerInvoices.AnyAsync(x => x.Id == invoiceId && x.JournalEntry != null && x.JournalEntry.IsPosted);
                    if (!posted) return AssistantProposal.Refused($"{number} is still a draft; correct it instead of crediting it.");
                    if (model.Outstanding <= 0m)
                        return AssistantProposal.Refused($"{number} has nothing outstanding. Amounts already paid are refunded, not credited.");

                    model.CreditNoteDate = Args.Date(args, "date") ?? DateTime.Today;
                    model.Reason = Args.Required(args, "reason");
                    model.ReasonCode = Args.Str(args, "reason_code") switch
                    {
                        "SupplyCancelled" => CreditNoteReasons.SupplyCancelled,
                        "ChargedInError" => CreditNoteReasons.ChargedInError,
                        _ => CreditNoteReasons.ConsiderationAltered
                    };
                    var net = Args.Dec(args, "net_amount");
                    model.CreditFullOutstanding = net == null;
                    model.NetAmount = net ?? model.InvoiceSubtotal;
                    if (net is <= 0m) return AssistantProposal.Refused("The amount to credit must be above zero.");
                    var vat = VatDeterminationService.CalculateTax(model.NetAmount, model.VatCategory);
                    if (net != null && net.Value + vat > model.Outstanding + 0.01m)
                        return AssistantProposal.Refused($"{number} has {model.Outstanding:N2} outstanding including VAT; {net.Value + vat:N2} cannot be credited.");
                    var closed = await AssistantWriteTools.ClosedPeriodMessageAsync(ctx.Db, model.CreditNoteDate, "A credit note");
                    if (closed != null) return AssistantProposal.Refused(closed);

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Draft credit note on invoice {model.InvoiceNumber} ({model.TenantName}, {model.PropertyUnit}, contract {model.ContractNumber}) " +
                                  $"dated {model.CreditNoteDate:dd/MM/yyyy}: " +
                                  (model.CreditFullOutstanding ? $"the whole outstanding {model.Outstanding:N2}" : $"{model.NetAmount:N2} + VAT {vat:N2}") +
                                  $" {ctx.Currency}, because \"{model.Reason}\". Post it from Credit Notes to reduce the invoice and reverse the VAT.",
                        Normalized = model
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var model = args.Deserialize<CreditNoteEditViewModel>(Json)!;
                    model.Id = 0;
                    model.PostNow = false;
                    var state = new ModelStateDictionary();
                    var note = await ctx.Get<CreditNoteService>().SaveAsync(state, ctx.CompanyId, ctx.UserId, model);
                    if (note == null)
                        return AssistantActionResult.Failed(string.Join(" ", state.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Distinct()));
                    return new AssistantActionResult
                    {
                        Ok = true, Reference = note.CreditNoteNumber, Link = "/CustomerCreditNotes",
                        Message = $"Credit note {note.CreditNoteNumber} saved as a draft. Post it from Credit Notes."
                    };
                }
            };

            // ---------------------------------------------------------------- property
            yield return new AssistantTool
            {
                Name = "propose_create_property",
                Description = "Prepare a new property (building, villa compound, land) for the user to confirm. Ask for the name, property type, whether it is " +
                              "owned or managed for an owner (then the owner's name and commission %), the emirate and area. The code is generated when saved. " +
                              "Then offer propose_create_units.",
                Screen = ("Properties", "Create"),
                IsWrite = true,
                Parameters = Schema.Object(
                    ("name", Schema.Str("Property name"), true),
                    ("property_type", Schema.Enum("Type", "Residential", "Commercial", "Warehouse", "MixedUse"), true),
                    ("ownership_type", Schema.Enum("Owned by the company, or Managed for an owner", "Owned", "Managed"), true),
                    ("emirate", Schema.Enum("Emirate", PropertyRegistrationLists.Emirates), false),
                    ("area", Schema.Str("Area or community"), false),
                    ("address", Schema.Str("Address"), false),
                    ("owner_name", Schema.Str("Landlord, for a managed property"), false),
                    ("owner_trn", Schema.Str("Landlord's VAT TRN, if registered"), false),
                    ("commission_percent", Schema.Num("Management commission %, for a managed property"), false),
                    ("vat_category", Schema.Enum("VAT treatment of its rents", "Standard", "Exempt", "ZeroRated"), false),
                    ("plot_number", Schema.Str("Plot number"), false),
                    ("title_deed_number", Schema.Str("Title deed number"), false),
                    ("number_of_floors", Schema.Int("Floors"), false),
                    ("asset_value", Schema.Num("Asset value"), false)),
                Propose = async (ctx, args) =>
                {
                    var property = PropertyFrom(args);
                    if (property.OwnershipType == "Managed" && string.IsNullOrWhiteSpace(property.OwnerName))
                        return AssistantProposal.Refused("A managed property needs the owner's name.");
                    var state = new ModelStateDictionary();
                    await ctx.Get<MasterDataUniquenessService>().ValidatePropertyAsync(state, ctx.CompanyId, property);
                    if (!state.IsValid) return AssistantProposal.Refused(string.Join(" ", state.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
                    if (property.TitleDeedNumber != null &&
                        await ctx.Db.Properties.AnyAsync(x => x.TitleDeedNumber == property.TitleDeedNumber && x.Emirate == property.Emirate))
                        return AssistantProposal.Refused($"Title deed {property.TitleDeedNumber} is already recorded on another property.");

                    var code = await ctx.Get<CodeGenerationService>().PreviewPropertyCodeAsync(property.OwnershipType, property.PropertyType);
                    var warnings = new List<string>();
                    if (property.OwnershipType == "Managed" && property.CommissionPercentage == 0m)
                        warnings.Add("No management commission is set; the company would earn nothing on this property's rent.");
                    if (property.Emirate == null) warnings.Add("No emirate is given.");
                    if (property.PropertyType == "Residential" && property.VatCategory == VatCategories.Standard)
                        warnings.Add("Residential rent is usually exempt from VAT; check the VAT treatment before confirming.");

                    return new AssistantProposal
                    {
                        Ok = true,
                        Summary = $"Create property \"{property.Name}\" ({property.PropertyType}, {property.OwnershipType}" +
                                  (property.OwnershipType == "Managed" ? $" for {property.OwnerName}, commission {property.CommissionPercentage:0.##}%" : string.Empty) +
                                  $"){(property.AreaName != null || property.Emirate != null ? $" in {string.Join(", ", new[] { property.AreaName, property.Emirate }.Where(x => x != null))}" : string.Empty)}, " +
                                  $"VAT {property.VatCategory}. It will be coded {code}. Units are added afterwards.",
                        Warnings = warnings,
                        Normalized = args
                    };
                },
                Execute = async (ctx, args) =>
                {
                    var property = PropertyFrom(args);
                    var state = new ModelStateDictionary();
                    await ctx.Get<MasterDataUniquenessService>().ValidatePropertyAsync(state, ctx.CompanyId, property);
                    if (!state.IsValid) return AssistantActionResult.Failed(string.Join(" ", state.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));

                    property.CopyRegistrationFrom(property);
                    property.Code = await ctx.Get<CodeGenerationService>().GeneratePropertyCodeAsync(property.OwnershipType, property.PropertyType);
                    property.CompanyId = ctx.CompanyId;
                    property.IsActive = true;
                    ctx.Db.Properties.Add(property);
                    await ctx.Db.SaveChangesAsync();
                    return new AssistantActionResult
                    {
                        Ok = true, Reference = property.Code, Link = $"/Properties/Details/{property.Id}",
                        Message = $"Property {property.Code} {property.Name} created. Say if you want units added to it."
                    };
                }
            };
        }

        private static Property PropertyFrom(JsonElement args) => new()
        {
            Name = Args.Str(args, "name") ?? string.Empty,
            PropertyType = Args.Str(args, "property_type") ?? string.Empty,
            OwnershipType = Args.Str(args, "ownership_type") == "Managed" ? "Managed" : "Owned",
            Emirate = Args.Str(args, "emirate") is { } emirate
                ? PropertyRegistrationLists.Emirates.FirstOrDefault(x => string.Equals(x, emirate, StringComparison.OrdinalIgnoreCase)) ?? emirate
                : null,
            AreaName = Args.Str(args, "area"),
            Address = Args.Str(args, "address"),
            OwnerName = Args.Str(args, "owner_name"),
            OwnerTrn = Args.Str(args, "owner_trn"),
            CommissionPercentage = Args.Dec(args, "commission_percent") ?? 0m,
            VatCategory = Args.Str(args, "vat_category") ?? VatCategories.Standard,
            PlotNumber = Args.Str(args, "plot_number"),
            TitleDeedNumber = Args.Str(args, "title_deed_number"),
            NumberOfFloors = Args.Int(args, "number_of_floors"),
            AssetValue = Args.Dec(args, "asset_value") ?? 0m
        };

        /// <summary>The property and unit an expense is for, by code or number.</summary>
        private static async Task<(int? PropertyId, int? UnitId, string? Error, string Label)> PlaceAsync(AssistantToolContext ctx, string? property, string? unit)
        {
            int? propertyId = null, unitId = null;
            var label = string.Empty;
            if (unit != null)
            {
                var units = await ctx.Db.Units.Include(x => x.Property).Where(x => x.UnitNumber == unit).Take(2).ToListAsync();
                if (units.Count != 1) return (null, null, $"Unit '{unit}' is not unique or not found.", label);
                unitId = units[0].Id;
                propertyId = units[0].PropertyId;
                label = $", unit {units[0].Property?.Code}/{units[0].UnitNumber}";
            }
            else if (property != null)
            {
                var found = await ctx.Db.Properties.Where(x => x.Code == property || x.Name == property).Select(x => new { x.Id, x.Code }).FirstOrDefaultAsync();
                if (found == null) return (null, null, $"No property '{property}'.", label);
                propertyId = found.Id;
                label = $", property {found.Code}";
            }

            return (propertyId, unitId, null, label);
        }
    }
}
