using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The investment property note for the financial statements, under the cost model
    /// (IAS 40): investment property is carried at cost less accumulated depreciation, and
    /// its fair value is disclosed alongside rather than booked.
    ///
    /// The movement table is read from the two ledger accounts - 15100 Investment Property
    /// and 15150 Accumulated Depreciation - so it agrees with the balance sheet. The fair
    /// values come from the latest valuation recorded on each property.
    /// </summary>
    public class InvestmentPropertyNoteService
    {
        private readonly ApplicationDbContext _db;

        public InvestmentPropertyNoteService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<InvestmentPropertyNoteDto> GetNoteAsync(DateTime fromDate, DateTime toDate)
        {
            var from = fromDate.Date;
            var to = toDate.Date;
            if (from > to)
                throw new InvalidOperationException("The start of the period cannot be after its end.");

            var costAccount = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == "15100");
            var depreciationAccount = await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(x => x.Code == "15150");
            if (costAccount == null || depreciationAccount == null)
                throw new InvalidOperationException("Accounts 15100 and 15150 are needed for the note. Restart the application once so they are added.");

            var lines = await _db.JournalEntryLines.AsNoTracking()
                .Where(x => (x.AccountId == costAccount.Id || x.AccountId == depreciationAccount.Id) &&
                            x.JournalEntry != null && x.JournalEntry.IsPosted && x.JournalEntry.EntryDate <= to)
                .Select(x => new
                {
                    x.AccountId,
                    x.PropertyId,
                    x.Debit,
                    x.Credit,
                    x.JournalEntry!.EntryDate,
                    x.JournalEntry.SourceType
                })
                .ToListAsync();

            var cost = lines.Where(x => x.AccountId == costAccount.Id).ToList();
            var depreciation = lines.Where(x => x.AccountId == depreciationAccount.Id).ToList();

            // Accumulated depreciation is a credit balance; shown positive.
            var note = new InvestmentPropertyNoteDto
            {
                FromDate = from,
                ToDate = to,
                OpeningCost = costAccount.OpeningBalance + cost.Where(x => x.EntryDate < from).Sum(x => x.Debit - x.Credit),
                // Allocating the opening balance to buildings moves nothing - it is left out of the movement.
                Additions = cost.Where(x => x.EntryDate >= from && x.SourceType != DevelopmentTransferService.SourceType && x.SourceType != InvestmentPropertySetupService.SourceType).Sum(x => x.Debit),
                TransfersFromDevelopment = cost.Where(x => x.EntryDate >= from && x.SourceType == DevelopmentTransferService.SourceType).Sum(x => x.Debit - x.Credit),
                Disposals = cost.Where(x => x.EntryDate >= from && x.SourceType != DevelopmentTransferService.SourceType && x.SourceType != InvestmentPropertySetupService.SourceType).Sum(x => x.Credit),
                OpeningDepreciation = -depreciationAccount.OpeningBalance + depreciation.Where(x => x.EntryDate < from).Sum(x => x.Credit - x.Debit),
                DepreciationCharge = depreciation.Where(x => x.EntryDate >= from && x.SourceType == "FixedAssetDepreciation").Sum(x => x.Credit - x.Debit),
                DepreciationOther = depreciation.Where(x => x.EntryDate >= from && x.SourceType != "FixedAssetDepreciation").Sum(x => x.Credit - x.Debit)
            };

            var properties = await _db.Properties.AsNoTracking()
                .Where(x => x.OwnershipType == "Owned")
                .OrderBy(x => x.Code)
                .Select(x => new { x.Id, x.Code, x.Name, x.FairValue, x.FairValueDate, x.ValuerName })
                .ToListAsync();

            foreach (var property in properties)
            {
                var propertyCost = cost.Where(x => x.PropertyId == property.Id).Sum(x => x.Debit - x.Credit);
                var propertyDepreciation = depreciation.Where(x => x.PropertyId == property.Id).Sum(x => x.Credit - x.Debit);

                if (propertyCost == 0m && propertyDepreciation == 0m && property.FairValue == null)
                    continue;

                note.Properties.Add(new InvestmentPropertyRow
                {
                    PropertyId = property.Id,
                    Label = $"{property.Code} - {property.Name}",
                    Cost = propertyCost,
                    AccumulatedDepreciation = propertyDepreciation,
                    FairValue = property.FairValue,
                    FairValueDate = property.FairValueDate,
                    Valuer = property.ValuerName
                });
            }

            // Opening balances brought forward and anything posted without a property.
            var unallocatedCost = costAccount.OpeningBalance + cost.Where(x => x.PropertyId == null).Sum(x => x.Debit - x.Credit);
            var unallocatedDepreciation = -depreciationAccount.OpeningBalance + depreciation.Where(x => x.PropertyId == null).Sum(x => x.Credit - x.Debit);
            if (unallocatedCost != 0m || unallocatedDepreciation != 0m)
            {
                note.Properties.Add(new InvestmentPropertyRow
                {
                    Label = "Not allocated to a property (opening balances and untagged entries)",
                    Cost = unallocatedCost,
                    AccumulatedDepreciation = unallocatedDepreciation
                });
            }

            return note;
        }
    }

    public class InvestmentPropertyNoteDto
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }

        public decimal OpeningCost { get; set; }
        public decimal Additions { get; set; }
        public decimal TransfersFromDevelopment { get; set; }
        public decimal Disposals { get; set; }
        public decimal ClosingCost => OpeningCost + Additions + TransfersFromDevelopment - Disposals;

        public decimal OpeningDepreciation { get; set; }
        public decimal DepreciationCharge { get; set; }
        public decimal DepreciationOther { get; set; }
        public decimal ClosingDepreciation => OpeningDepreciation + DepreciationCharge + DepreciationOther;

        public decimal OpeningCarryingAmount => OpeningCost - OpeningDepreciation;
        public decimal ClosingCarryingAmount => ClosingCost - ClosingDepreciation;

        public List<InvestmentPropertyRow> Properties { get; set; } = new();

        public decimal TotalFairValue => Properties.Sum(x => x.FairValue ?? 0m);
        public bool AllValued => Properties.Where(x => x.PropertyId.HasValue).All(x => x.FairValue.HasValue);
    }

    public class InvestmentPropertyRow
    {
        public int? PropertyId { get; set; }
        public string Label { get; set; } = string.Empty;
        public decimal Cost { get; set; }
        public decimal AccumulatedDepreciation { get; set; }
        public decimal CarryingAmount => Cost - AccumulatedDepreciation;
        public decimal? FairValue { get; set; }
        public DateTime? FairValueDate { get; set; }
        public string? Valuer { get; set; }
    }
}
