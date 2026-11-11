using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Car parking: a property's numbered bays, giving a bay to a contract for a charge,
    /// and the board of which bays are taken (see <see cref="ParkingAssignment"/>).
    /// </summary>
    public class ParkingService
    {
        private readonly ApplicationDbContext _db;

        public ParkingService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  THE BOARD
        // =====================================================================

        public sealed class BayRow
        {
            public int Id { get; set; }
            public string Number { get; set; } = string.Empty;
            public string? Type { get; set; }
            public string? Notes { get; set; }
            public bool IsActive { get; set; }

            public int? AssignmentId { get; set; }
            public string? Tenant { get; set; }
            public string? Unit { get; set; }
            public string? ContractNumber { get; set; }
            public string? VehiclePlate { get; set; }
            public DateTime? Until { get; set; }
            public decimal AnnualCharge { get; set; }
            public int BillingFrequencyMonths { get; set; }

            public bool IsOccupied => AssignmentId.HasValue;
        }

        public sealed class PropertyBoard
        {
            public int PropertyId { get; set; }
            public string Property { get; set; } = string.Empty;
            public List<BayRow> Bays { get; set; } = new();

            public int Total => Bays.Count(x => x.IsActive);
            public int Occupied => Bays.Count(x => x.IsActive && x.IsOccupied);
            public int Vacant => Total - Occupied;
            public int Closed => Bays.Count(x => !x.IsActive);
            public decimal AnnualIncome => Bays.Where(x => x.IsOccupied).Sum(x => x.AnnualCharge);
        }

        /// <summary>Every property that has bays, or the one asked for, with each bay and who has it.</summary>
        public async Task<List<PropertyBoard>> BoardAsync(int companyId, int? propertyId = null)
        {
            var bays = await _db.ParkingBays.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && (propertyId == null || x.PropertyId == propertyId))
                .Select(x => new
                {
                    x.Id, x.PropertyId, Property = x.Property!.Code + " - " + x.Property.Name, x.BayNumber, x.BayType, x.Notes, x.IsActive,
                    Assignment = x.Assignments.Where(a => a.Status == ParkingAssignmentStatuses.Active)
                        .Select(a => new
                        {
                            a.Id, Tenant = a.Lease!.Tenant!.FullName, Unit = a.Lease.Unit!.UnitNumber, a.Lease.ContractNumber,
                            a.VehiclePlate, a.EndDate, a.AnnualCharge, a.BillingFrequencyMonths
                        })
                        .FirstOrDefault()
                })
                .ToListAsync();

            return bays
                .GroupBy(x => new { x.PropertyId, x.Property })
                .OrderBy(g => g.Key.Property)
                .Select(g => new PropertyBoard
                {
                    PropertyId = g.Key.PropertyId,
                    Property = g.Key.Property,
                    Bays = g.OrderBy(x => x.BayNumber, NaturalOrder.Instance).Select(x => new BayRow
                    {
                        Id = x.Id, Number = x.BayNumber, Type = x.BayType, Notes = x.Notes, IsActive = x.IsActive,
                        AssignmentId = x.Assignment?.Id, Tenant = x.Assignment?.Tenant, Unit = x.Assignment?.Unit,
                        ContractNumber = x.Assignment?.ContractNumber, VehiclePlate = x.Assignment?.VehiclePlate,
                        Until = x.Assignment?.EndDate, AnnualCharge = x.Assignment?.AnnualCharge ?? 0m,
                        BillingFrequencyMonths = x.Assignment?.BillingFrequencyMonths ?? 0
                    }).ToList()
                })
                .ToList();
        }

        /// <summary>"B1-2" before "B1-10": bay numbers sort as people read them.</summary>
        private sealed class NaturalOrder : IComparer<string>
        {
            public static readonly NaturalOrder Instance = new();

            public int Compare(string? a, string? b)
            {
                static string Pad(string? s) => System.Text.RegularExpressions.Regex.Replace(s ?? string.Empty, @"\d+", m => m.Value.PadLeft(10, '0'));
                return string.Compare(Pad(a), Pad(b), StringComparison.OrdinalIgnoreCase);
            }
        }

        // =====================================================================
        //  THE BAYS
        // =====================================================================

        /// <summary>
        /// Adds bays to a property: one number, or a run of them ("B1-" from 1 to 40 makes
        /// B1-001 ... B1-040). Numbers the property already has are skipped.
        /// </summary>
        public async Task<(bool Ok, string Message)> AddBaysAsync(int companyId, int propertyId, string? prefix, int? from, int? to, string? type)
        {
            if (!await _db.Properties.IgnoreQueryFilters().AnyAsync(x => x.Id == propertyId && x.CompanyId == companyId))
                return (false, "Choose the property.");
            if (!string.IsNullOrWhiteSpace(type) && !ParkingBayTypes.All.Contains(type))
                return (false, "Choose the kind of bay from the list.");

            prefix = (prefix ?? string.Empty).Trim();
            var numbers = new List<string>();
            if (from.HasValue || to.HasValue)
            {
                var first = from ?? to!.Value;
                var last = to ?? from!.Value;
                if (first < 0 || last < first) return (false, "Give the first and the last number of the run, the first not above the last.");
                if (last - first >= 500) return (false, "Add at most 500 bays at a time.");
                var width = Math.Max(2, last.ToString().Length);
                for (var n = first; n <= last; n++)
                    numbers.Add(prefix + n.ToString().PadLeft(width, '0'));
            }
            else
            {
                if (prefix.Length == 0) return (false, "Type the bay number, or the first and last number of a run.");
                numbers.Add(prefix);
            }

            if (numbers.Any(x => x.Length > ParkingBay.MaxNumberLength))
                return (false, $"Keep bay numbers within {ParkingBay.MaxNumberLength} characters.");

            var existing = (await _db.ParkingBays.IgnoreQueryFilters()
                    .Where(x => x.PropertyId == propertyId).Select(x => x.BayNumber).ToListAsync())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var added = 0;
            foreach (var number in numbers.Where(x => existing.Add(x)))
            {
                _db.ParkingBays.Add(new ParkingBay
                {
                    CompanyId = companyId,
                    PropertyId = propertyId,
                    BayNumber = number,
                    BayType = string.IsNullOrWhiteSpace(type) ? null : type
                });
                added++;
            }

            if (added == 0)
                return (false, numbers.Count == 1 ? "The property already has that bay." : "The property already has all of those bays.");

            await _db.SaveChangesAsync();
            var skipped = numbers.Count - added;
            return (true, (added == 1 ? "1 bay added." : $"{added} bays added.") + (skipped > 0 ? $" {skipped} already existed and were skipped." : string.Empty));
        }

        /// <summary>Closes a bay (it is not let any more) or opens it again. An occupied bay is released first.</summary>
        public async Task<(bool Ok, string Message)> ToggleBayAsync(int companyId, int bayId)
        {
            var bay = await _db.ParkingBays.IgnoreQueryFilters()
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == bayId && x.CompanyId == companyId);
            if (bay == null) return (false, "The bay was not found.");
            if (bay.IsActive && bay.Assignments.Any(x => x.Status == ParkingAssignmentStatuses.Active))
                return (false, $"Bay {bay.BayNumber} is taken. Release it first, then close it.");

            bay.IsActive = !bay.IsActive;
            await _db.SaveChangesAsync();
            return (true, bay.IsActive ? $"Bay {bay.BayNumber} is open again." : $"Bay {bay.BayNumber} is closed and no longer offered.");
        }

        // =====================================================================
        //  GIVING A BAY TO A CONTRACT
        // =====================================================================

        public sealed record LeaseOption(int Id, string Label, DateTime EndDate, string VatCategory);

        /// <summary>Contracts in force on a property: the ones a bay there can be given to.</summary>
        public async Task<List<LeaseOption>> LeasesAsync(int companyId, int propertyId) =>
            (await _db.Leases.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Status == LeaseStatuses.Active && x.Unit!.PropertyId == propertyId)
                .OrderBy(x => x.Unit!.UnitNumber)
                .Select(x => new { x.Id, Unit = x.Unit!.UnitNumber, Tenant = x.Tenant!.FullName, x.ContractNumber, x.EndDate, x.VatCategory })
                .ToListAsync())
            .Select(x => new LeaseOption(x.Id, $"{x.Unit} - {x.Tenant} ({x.ContractNumber})", x.EndDate, x.VatCategory))
            .ToList();

        public sealed class AssignInput
        {
            public int BayId { get; set; }
            public int LeaseId { get; set; }
            public DateTime? StartDate { get; set; }
            public decimal AnnualCharge { get; set; }
            public int BillingFrequencyMonths { get; set; }
            public string? VatCategory { get; set; }
            public string? VehiclePlate { get; set; }
            public string? Notes { get; set; }
        }

        /// <summary>
        /// The invoices a charge will produce from the start date to the end of the contract,
        /// without saving: one for the whole period, or one every so many months, each the
        /// yearly charge divided by the number of payments in a year - as rent is billed.
        /// </summary>
        public static List<(DateTime Date, DateTime PeriodEnd, decimal Net)> Plan(DateTime start, DateTime end, decimal annualCharge, int frequencyMonths)
        {
            var plan = new List<(DateTime, DateTime, decimal)>();
            if (annualCharge <= 0m || end < start)
                return plan;

            if (frequencyMonths == 0)
            {
                plan.Add((start, end, Math.Round(annualCharge * MonthsBetween(start, end) / 12m, 2)));
                return plan;
            }

            var each = Math.Round(annualCharge / (12m / frequencyMonths), 2);
            for (var date = start; date <= end; date = date.AddMonths(frequencyMonths))
            {
                var periodEnd = date.AddMonths(frequencyMonths).AddDays(-1);
                plan.Add((date, periodEnd > end ? end : periodEnd, each));
            }
            return plan;
        }

        /// <summary>Months a period covers, a part month counting as a whole one: 1 Mar to 28 Feb is 12.</summary>
        private static int MonthsBetween(DateTime start, DateTime end) =>
            Math.Max(1, (end.Year - start.Year) * 12 + end.Month - start.Month + (end.Day >= start.Day - 1 ? 1 : 0));

        public async Task<(bool Ok, string Message)> AssignAsync(int companyId, AssignInput input, string? byName)
        {
            var bay = await _db.ParkingBays.IgnoreQueryFilters()
                .Include(x => x.Assignments)
                .FirstOrDefaultAsync(x => x.Id == input.BayId && x.CompanyId == companyId);
            if (bay == null) return (false, "Choose the bay.");
            if (!bay.IsActive) return (false, $"Bay {bay.BayNumber} is closed.");
            if (bay.Assignments.Any(x => x.Status == ParkingAssignmentStatuses.Active))
                return (false, $"Bay {bay.BayNumber} is already taken.");

            var lease = await _db.Leases.IgnoreQueryFilters()
                .Include(x => x.Unit)!.ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == input.LeaseId && x.CompanyId == companyId);
            if (lease == null || lease.Status != LeaseStatuses.Active)
                return (false, "Choose a contract that is in force.");
            if (lease.Unit!.PropertyId != bay.PropertyId)
                return (false, "The contract is for a unit in another property.");

            var start = (input.StartDate ?? DateTime.Today).Date;
            if (start < lease.StartDate.Date) start = lease.StartDate.Date;
            if (start > lease.EndDate.Date) return (false, "The start date is after the contract ends.");
            if (input.AnnualCharge < 0m) return (false, "The charge cannot be negative.");
            if (!ParkingAssignment.Frequencies.Contains(input.BillingFrequencyMonths)) return (false, "Choose the payment terms from the list.");

            var category = VatCategories.All.Contains(input.VatCategory) ? input.VatCategory! : lease.VatCategory;
            var company = await _db.Companies.IgnoreQueryFilters().AsNoTracking().FirstAsync(x => x.Id == companyId);
            if (!company.IsVatRegistered && category == VatCategories.Standard)
                category = VatCategories.Exempt;

            var assignment = new ParkingAssignment
            {
                CompanyId = companyId,
                ParkingBayId = bay.Id,
                LeaseId = lease.Id,
                StartDate = start,
                EndDate = lease.EndDate.Date,
                AnnualCharge = Math.Round(input.AnnualCharge, 2),
                BillingFrequencyMonths = input.BillingFrequencyMonths,
                VatCategory = category,
                VehiclePlate = Trim(input.VehiclePlate, 30),
                Notes = Trim(input.Notes, 300),
                CreatedByName = byName
            };
            _db.ParkingAssignments.Add(assignment);
            await _db.SaveChangesAsync();

            // The charge, billed on the contract and earned month by month, as rent is.
            var plan = Plan(assignment.StartDate, assignment.EndDate, assignment.AnnualCharge, assignment.BillingFrequencyMonths);
            var number = 0;
            foreach (var (date, periodEnd, net) in plan)
            {
                var vat = VatDeterminationService.CalculateTax(net, category);
                _db.CustomerInvoices.Add(new CustomerInvoice
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    InvoiceNumber = $"PRK-{lease.ContractNumber}-{assignment.Id}-{++number:D2}",
                    InvoiceDate = date,
                    DueDate = date,
                    DateOfSupply = date,
                    PeriodStart = date,
                    PeriodEnd = periodEnd,
                    LineDescription = $"Car parking, bay {bay.BayNumber}",
                    ParkingAssignmentId = assignment.Id,
                    Subtotal = net,
                    VatAmount = vat,
                    TotalAmount = net + vat,
                    VatCategory = category,
                    VatRate = VatDeterminationService.RateFor(category),
                    Status = "Draft"
                });
            }

            var total = plan.Sum(x => x.Net);
            if (total > 0m)
            {
                // For a managed property the charge is the owner's; only the commission is income.
                var property = lease.Unit.Property!;
                var income = property.OwnershipType == "Managed" ? Math.Round(total * property.CommissionPercentage / 100m, 2) : total;
                var months = new List<DateTime>();
                for (var month = new DateTime(start.Year, start.Month, 1); month <= assignment.EndDate; month = month.AddMonths(1))
                    months.Add(new DateTime(month.Year, month.Month, DateTime.DaysInMonth(month.Year, month.Month)));

                var each = Math.Round(income / months.Count, 2);
                for (var i = 0; i < months.Count; i++)
                {
                    _db.RevenueRecognitionSchedules.Add(new RevenueRecognitionSchedule
                    {
                        CompanyId = companyId,
                        LeaseId = lease.Id,
                        ParkingAssignmentId = assignment.Id,
                        RecognitionDate = months[i],
                        // The last month takes the cents the division left over.
                        Amount = i == months.Count - 1 ? income - each * (months.Count - 1) : each,
                        Status = "Planned"
                    });
                }
            }

            await _db.SaveChangesAsync();

            return (true, plan.Count == 0
                ? $"Bay {bay.BayNumber} given to contract {lease.ContractNumber}, free of charge."
                : $"Bay {bay.BayNumber} given to contract {lease.ContractNumber}. {plan.Count} invoice(s) for {total:N2} before VAT were created as drafts: post each from Customer Invoices when it falls due.");
        }

        /// <summary>
        /// Takes a bay back. Invoices dated after that day that are still drafts are removed
        /// with the income planned for the months after it; what is already posted stays, for
        /// the accounts to credit if the tenant is owed anything.
        /// </summary>
        public async Task<(bool Ok, string Message)> ReleaseAsync(int companyId, int assignmentId, DateTime? on)
        {
            var assignment = await _db.ParkingAssignments.IgnoreQueryFilters()
                .Include(x => x.ParkingBay)
                .FirstOrDefaultAsync(x => x.Id == assignmentId && x.CompanyId == companyId);
            if (assignment == null) return (false, "The assignment was not found.");
            if (assignment.Status != ParkingAssignmentStatuses.Active) return (false, "The bay has already been released.");

            var day = (on ?? DateTime.Today).Date;
            if (day < assignment.StartDate) day = assignment.StartDate;

            var (removed, kept) = await EndAsync(assignment, day);
            await _db.SaveChangesAsync();

            return (true, $"Bay {assignment.ParkingBay!.BayNumber} released." +
                          (removed > 0 ? $" {removed} draft invoice(s) dated after {day:dd/MM/yyyy} were removed." : string.Empty) +
                          (kept > 0 ? $" {kept} posted invoice(s) dated after that day remain: issue a credit note if the tenant is owed a refund." : string.Empty));
        }

        /// <summary>Frees every bay of a contract that has ended. The caller saves.</summary>
        public async Task ReleaseForLeaseAsync(int leaseId, DateTime on)
        {
            var assignments = await _db.ParkingAssignments.IgnoreQueryFilters()
                .Where(x => x.LeaseId == leaseId && x.Status == ParkingAssignmentStatuses.Active)
                .ToListAsync();
            foreach (var assignment in assignments)
                await EndAsync(assignment, on.Date < assignment.StartDate ? assignment.StartDate : on.Date);
        }

        private async Task<(int Removed, int Kept)> EndAsync(ParkingAssignment assignment, DateTime day)
        {
            assignment.Status = ParkingAssignmentStatuses.Released;
            assignment.ReleasedOn = day;

            var later = await _db.CustomerInvoices.IgnoreQueryFilters()
                .Include(x => x.JournalEntry)
                .Where(x => x.ParkingAssignmentId == assignment.Id && x.InvoiceDate > day && x.Status != "Cancelled")
                .ToListAsync();
            var drafts = later.Where(x => x.JournalEntryId == null && x.AmountPaid == 0m).ToList();
            _db.CustomerInvoices.RemoveRange(drafts);

            var planned = await _db.RevenueRecognitionSchedules.IgnoreQueryFilters()
                .Where(x => x.ParkingAssignmentId == assignment.Id && x.Status == "Planned" && x.JournalEntryId == null &&
                            x.RecognitionDate > new DateTime(day.Year, day.Month, DateTime.DaysInMonth(day.Year, day.Month)))
                .ToListAsync();
            _db.RevenueRecognitionSchedules.RemoveRange(planned);

            return (drafts.Count, later.Count - drafts.Count);
        }

        private static string? Trim(string? text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            var clean = text.Trim();
            return clean.Length > max ? clean[..max] : clean;
        }
    }
}
