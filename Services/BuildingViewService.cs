using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The building view: every unit of a property placed on its floor and coloured by the
    /// health of its rent, with the facts behind the colour.
    ///
    /// The tenant's score is the Tenant Health report's own 0-100 score, so the drawing and
    /// the report always agree. The unit's own contract adds the hard signals:
    ///
    ///   At risk   score below 55, or rent overdue more than 90 days, or a cheque on the
    ///             contract bounced in the last 12 months
    ///   Watch     score below 75, or rent overdue more than 30 days, or a request to
    ///             postpone a payment in the last 12 months
    ///   Healthy   none of these
    ///
    /// Units without a current contract are vacant, under maintenance, reserved for a buyer
    /// or sold.
    /// </summary>
    public class BuildingViewService
    {
        public const decimal RiskScore = 55m;
        public const decimal WatchScore = 75m;
        public const int RiskOverdueDays = 90;
        public const int WatchOverdueDays = 30;

        private static readonly Regex Digits = new(@"-?\d+");

        private readonly ApplicationDbContext _db;
        private readonly ICurrentCompanyAccessor _currentCompany;
        private readonly ManagementReportService _reports;

        public BuildingViewService(ApplicationDbContext db, ICurrentCompanyAccessor currentCompany, ManagementReportService reports)
        {
            _db = db;
            _currentCompany = currentCompany;
            _reports = reports;
        }

        private int CompanyId => _currentCompany.CompanyId
            ?? throw new InvalidOperationException("No company is selected for the current user.");

        /// <summary>Every active property with the count of units in each state, for the portfolio page.</summary>
        public async Task<List<BuildingSummaryDto>> GetPortfolioAsync()
        {
            var scores = await TenantScoresAsync();
            var ids = await _db.Properties.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Code)
                .Select(x => x.Id)
                .ToListAsync();

            var result = new List<BuildingSummaryDto>();
            foreach (var id in ids)
            {
                var building = await BuildAsync(id, scores);
                if (building != null)
                    result.Add(new BuildingSummaryDto(building));
            }
            return result;
        }

        public async Task<BuildingViewDto?> GetBuildingAsync(int propertyId)
        {
            return await BuildAsync(propertyId, await TenantScoresAsync());
        }

        private async Task<Dictionary<int, (decimal Score, string Grade)>> TenantScoresAsync()
        {
            var report = await _reports.GetTenantHealthAsync(CompanyId, DateTime.Today);
            return report.Rows.Where(x => x.IsScored).ToDictionary(x => x.TenantId, x => (x.HealthScore, x.HealthGrade));
        }

        private async Task<BuildingViewDto?> BuildAsync(int propertyId, Dictionary<int, (decimal Score, string Grade)> scores)
        {
            var today = DateTime.Today;
            var yearAgo = today.AddYears(-1);

            var property = await _db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == propertyId);
            if (property == null)
                return null;

            var units = await _db.Units.AsNoTracking()
                .Where(x => x.PropertyId == propertyId && x.IsActive)
                .ToListAsync();
            var unitIds = units.Select(x => x.Id).ToList();

            var leases = await _db.Leases.AsNoTracking()
                .Include(x => x.Tenant)
                .Where(x => unitIds.Contains(x.UnitId) &&
                            x.Status != LeaseStatuses.Draft && x.Status != LeaseStatuses.PendingApproval)
                .ToListAsync();

            var current = leases
                .Where(x => x.Status == LeaseStatuses.Active && x.StartDate <= today && x.EndDate >= today)
                .GroupBy(x => x.UnitId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.StartDate).First());
            var currentIds = current.Values.Select(x => x.Id).ToList();

            var invoices = await _db.CustomerInvoices.AsNoTracking()
                .Where(x => currentIds.Contains(x.LeaseId) && x.JournalEntry != null && x.JournalEntry.IsPosted && x.Status != "Cancelled")
                .Select(x => new { x.LeaseId, x.DueDate, x.TotalAmount, x.AmountPaid })
                .ToListAsync();

            var bounced = await _db.PdcReceipts.AsNoTracking()
                .Where(x => currentIds.Contains(x.LeaseId) && (x.Status == "Bounced" || x.BouncedDate != null) && x.ChequeDate >= yearAgo)
                .Select(x => new { x.LeaseId, x.Amount })
                .ToListAsync();

            var postponements = await _db.TenantServiceRequests.AsNoTracking()
                .Where(x => x.RequestType == ServiceRequestTypes.PaymentPostponement && x.SubmittedAt >= yearAgo &&
                            (x.LeaseId != null && currentIds.Contains(x.LeaseId.Value) || x.UnitId != null && unitIds.Contains(x.UnitId.Value)))
                .Select(x => new { x.LeaseId, x.UnitId, x.TenantId })
                .ToListAsync();

            var sales = await _db.UnitSales.AsNoTracking()
                .Include(x => x.Buyer)
                .Where(x => unitIds.Contains(x.UnitId) && x.Status != UnitSaleStatuses.Cancelled)
                .ToListAsync();

            var dto = new BuildingViewDto
            {
                PropertyId = property.Id,
                Code = property.Code,
                Name = property.Name,
                Address = property.Address,
                OwnershipType = property.OwnershipType,
                PropertyType = property.PropertyType,
                AsOf = today
            };

            foreach (var unit in units)
            {
                var row = new BuildingUnitDto
                {
                    UnitId = unit.Id,
                    UnitNumber = unit.UnitNumber,
                    ShortLabel = ShortLabel(unit.UnitNumber),
                    Floor = ResolveFloor(unit),
                    Position = unit.FloorPosition,
                    Side = unit.FloorSide,
                    AreaSqFt = unit.AreaSqFt,
                    Usage = unit.UsageType,
                    StandardAnnualRent = unit.StandardAnnualRent
                };

                if (current.TryGetValue(unit.Id, out var lease))
                {
                    var lines = invoices.Where(x => x.LeaseId == lease.Id).ToList();
                    var overdue = lines.Where(x => x.DueDate < today && x.AmountPaid < x.TotalAmount).ToList();

                    row.LeaseId = lease.Id;
                    row.ContractNumber = lease.ContractNumber;
                    row.TenantId = lease.TenantId;
                    row.TenantName = lease.Tenant?.FullName;
                    row.AnnualRent = lease.AnnualRent;
                    row.ContractEnd = lease.EndDate;
                    row.DaysToExpiry = (lease.EndDate - today).Days;
                    row.Invoiced = lines.Sum(x => x.TotalAmount);
                    row.Collected = lines.Sum(x => x.AmountPaid);
                    row.OverdueAmount = overdue.Sum(x => x.TotalAmount - x.AmountPaid);
                    row.OldestOverdueDays = overdue.Count == 0 ? 0 : (today - overdue.Min(x => x.DueDate)).Days;
                    row.BouncedCheques = bounced.Count(x => x.LeaseId == lease.Id);
                    row.BouncedAmount = bounced.Where(x => x.LeaseId == lease.Id).Sum(x => x.Amount);
                    row.Postponements = postponements.Count(x => x.LeaseId == lease.Id || x.UnitId == unit.Id && x.TenantId == lease.TenantId);
                    if (scores.TryGetValue(lease.TenantId, out var score))
                    {
                        row.Score = score.Score;
                        row.Grade = score.Grade;
                    }

                    Classify(row);
                }
                else
                {
                    var sale = sales.Where(x => x.UnitId == unit.Id).OrderByDescending(x => x.ContractDate).FirstOrDefault();
                    if (sale != null)
                    {
                        row.Health = sale.Status == UnitSaleStatuses.HandedOver ? "sold" : "reserved";
                        row.TenantName = sale.Buyer?.FullName;
                        row.ContractNumber = sale.ContractNumber;
                        row.SaleId = sale.Id;
                        row.Reasons.Add(sale.Status == UnitSaleStatuses.HandedOver
                            ? $"Sold - handed over on {sale.HandoverDate:dd/MM/yyyy}"
                            : $"Reserved - sale contract signed on {sale.ContractDate:dd/MM/yyyy}");
                    }
                    else if (unit.Status == "UnderMaintenance")
                    {
                        row.Health = "maintenance";
                        row.Reasons.Add("Under maintenance - not available to let");
                    }
                    else
                    {
                        row.Health = "vacant";
                        var lastEnd = leases.Where(x => x.UnitId == unit.Id && x.EndDate < today).Select(x => (DateTime?)x.EndDate).Max();
                        row.VacantDays = lastEnd.HasValue ? (today - lastEnd.Value).Days : null;
                        row.Reasons.Add(lastEnd.HasValue ? $"Vacant since {lastEnd.Value.AddDays(1):dd/MM/yyyy}" : "Vacant - never let");
                    }
                }

                dto.Units.Add(row);
            }

            // Floors: every floor from the lowest to the highest, including the building's recorded
            // height, so a tower with units on only some floors still looks like the tower.
            if (dto.Units.Count > 0)
            {
                var low = Math.Min(0, dto.Units.Min(x => x.Floor));
                var high = dto.Units.Max(x => x.Floor);
                if (property.NumberOfFloors is > 0)
                    high = Math.Max(high, low + property.NumberOfFloors.Value - 1);
                high = Math.Min(high, low + 79);

                for (var floor = high; floor >= low; floor--)
                {
                    dto.Floors.Add(new BuildingFloorDto
                    {
                        Number = floor,
                        Label = floor == 0 ? "G" : floor < 0 ? "B" + -floor : floor.ToString(),
                        Units = dto.Units.Where(x => x.Floor == floor)
                            .OrderBy(x => x.Position ?? int.MaxValue).ThenBy(x => x.UnitNumber)
                            .ToList()
                    });
                }
            }

            return dto;
        }

        private static void Classify(BuildingUnitDto row)
        {
            var risk = new List<string>();
            var watch = new List<string>();

            if (row.Score is { } score && score < RiskScore) risk.Add($"Tenant health score {score:0} / 100 (grade {row.Grade})");
            else if (row.Score is { } s && s < WatchScore) watch.Add($"Tenant health score {s:0} / 100 (grade {row.Grade})");

            if (row.OldestOverdueDays > RiskOverdueDays) risk.Add($"Rent overdue for {row.OldestOverdueDays} days ({row.OverdueAmount:N2})");
            else if (row.OldestOverdueDays > WatchOverdueDays) watch.Add($"Rent overdue for {row.OldestOverdueDays} days ({row.OverdueAmount:N2})");

            if (row.BouncedCheques > 0) risk.Add($"{row.BouncedCheques} bounced cheque(s) in the last 12 months ({row.BouncedAmount:N2})");
            if (row.Postponements > 0) watch.Add($"{row.Postponements} request(s) to postpone a payment in the last 12 months");

            if (risk.Count > 0)
            {
                row.Health = "risk";
                row.Reasons.AddRange(risk);
                row.Reasons.AddRange(watch);
            }
            else if (watch.Count > 0)
            {
                row.Health = "watch";
                row.Reasons.AddRange(watch);
            }
            else
            {
                row.Health = "good";
                row.Reasons.Add(row.OverdueAmount > 0 ? "Paying, with nothing more than 30 days overdue" : "Paid up to date");
            }
        }

        /// <summary>The floor number: as recorded, or read from the floor text (G = 0, B1 = -1, "3rd" = 3).</summary>
        public static int ResolveFloor(Unit unit)
        {
            if (unit.FloorNumber.HasValue)
                return unit.FloorNumber.Value;

            var text = (unit.Floor ?? string.Empty).Trim().ToUpperInvariant();
            if (text.StartsWith("B") && Digits.Match(text) is { Success: true } basement)
                return -Math.Abs(int.Parse(basement.Value));
            if (text is "G" or "GF" or "GROUND" || text.StartsWith("GROUND") || text.StartsWith("M"))
                return 0;
            var match = Digits.Match(text);
            return match.Success && int.TryParse(match.Value, out var n) ? n : 0;
        }

        /// <summary>The last part of the unit code, short enough to print on the drawing: OWN-R-0001-U03 → U03.</summary>
        private static string ShortLabel(string unitNumber)
        {
            var parts = unitNumber.Split('-', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? unitNumber : parts[^1];
        }
    }

    public class BuildingViewDto
    {
        public int PropertyId { get; set; }
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Address { get; set; }
        public string OwnershipType { get; set; } = string.Empty;
        public string PropertyType { get; set; } = string.Empty;
        public DateTime AsOf { get; set; }

        public List<BuildingFloorDto> Floors { get; set; } = new();
        public List<BuildingUnitDto> Units { get; set; } = new();

        public int Count(string health) => Units.Count(x => x.Health == health);
        public int Let => Units.Count(x => x.LeaseId.HasValue);
        public decimal OccupancyPercent => Units.Count == 0 ? 0m : Math.Round((decimal)Let / Units.Count * 100m, 1);
        public decimal AnnualRent => Units.Sum(x => x.AnnualRent);
        public decimal Overdue => Units.Sum(x => x.OverdueAmount);
    }

    public class BuildingFloorDto
    {
        public int Number { get; set; }
        public string Label { get; set; } = string.Empty;
        public List<BuildingUnitDto> Units { get; set; } = new();
    }

    public class BuildingUnitDto
    {
        public int UnitId { get; set; }
        public string UnitNumber { get; set; } = string.Empty;
        public string ShortLabel { get; set; } = string.Empty;
        public int Floor { get; set; }
        public int? Position { get; set; }

        /// <summary>Front, Back, Left or Right, for the 3D view; empty when not recorded.</summary>
        public string? Side { get; set; }
        public decimal AreaSqFt { get; set; }
        public string? Usage { get; set; }
        public decimal StandardAnnualRent { get; set; }

        /// <summary>good, watch, risk, vacant, maintenance, reserved or sold.</summary>
        public string Health { get; set; } = "vacant";
        public List<string> Reasons { get; set; } = new();

        public int? LeaseId { get; set; }
        public int? SaleId { get; set; }
        public string? ContractNumber { get; set; }
        public int? TenantId { get; set; }
        public string? TenantName { get; set; }
        public decimal AnnualRent { get; set; }
        public DateTime? ContractEnd { get; set; }
        public int? DaysToExpiry { get; set; }

        public decimal Invoiced { get; set; }
        public decimal Collected { get; set; }
        public decimal Outstanding => Invoiced - Collected;
        public decimal OverdueAmount { get; set; }
        public int OldestOverdueDays { get; set; }
        public int BouncedCheques { get; set; }
        public decimal BouncedAmount { get; set; }
        public int Postponements { get; set; }

        public decimal? Score { get; set; }
        public string? Grade { get; set; }
        public int? VacantDays { get; set; }

        public decimal CollectedPercent => Invoiced == 0m ? 100m : Math.Round(Collected / Invoiced * 100m, 0);
    }

    public class BuildingSummaryDto
    {
        public BuildingSummaryDto(BuildingViewDto b)
        {
            PropertyId = b.PropertyId;
            Code = b.Code;
            Name = b.Name;
            OwnershipType = b.OwnershipType;
            Units = b.Units.Count;
            Good = b.Count("good");
            Watch = b.Count("watch");
            Risk = b.Count("risk");
            Vacant = b.Count("vacant") + b.Count("maintenance");
            SoldOrReserved = b.Count("sold") + b.Count("reserved");
            OccupancyPercent = b.OccupancyPercent;
            Overdue = b.Overdue;
            AnnualRent = b.AnnualRent;
            Floors = b.Floors.Count;
        }

        public int PropertyId { get; }
        public string Code { get; }
        public string Name { get; }
        public string OwnershipType { get; }
        public int Units { get; }
        public int Floors { get; }
        public int Good { get; }
        public int Watch { get; }
        public int Risk { get; }
        public int Vacant { get; }
        public int SoldOrReserved { get; }
        public decimal OccupancyPercent { get; }
        public decimal Overdue { get; }
        public decimal AnnualRent { get; }
    }
}
