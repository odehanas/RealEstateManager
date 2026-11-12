using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// The rent of a stay priced per day, week or month (see <see cref="RentBases"/>).
    ///
    /// Both dates count: 1 to 7 January is seven days. The stay is billed in periods of
    /// one month from its first day - a stay of a month or less is one invoice - and each
    /// period is charged for the days it holds:
    ///
    ///   Daily     rate x days
    ///   Weekly    rate x days / 7
    ///   Monthly   the rate for a whole month; a part month in proportion to its days
    /// </summary>
    public static class StayRentCalculator
    {
        public sealed record Period(DateTime Start, DateTime End, decimal Amount)
        {
            public int Days => (End.Date - Start.Date).Days + 1;
        }

        public static int Days(DateTime start, DateTime end) => Math.Max(0, (end.Date - start.Date).Days + 1);

        /// <summary>The billing periods of the stay with the rent of each.</summary>
        public static List<Period> Periods(string basis, decimal rate, DateTime start, DateTime end)
        {
            var periods = new List<Period>();
            if (!RentBases.IsStay(basis) || rate <= 0m || end.Date < start.Date)
                return periods;

            for (var i = 0; ; i++)
            {
                // Always counted from the first day, so a stay starting on the 31st does not drift.
                var periodStart = start.Date.AddMonths(i);
                if (periodStart > end.Date)
                    break;

                var fullEnd = start.Date.AddMonths(i + 1).AddDays(-1);
                var periodEnd = fullEnd > end.Date ? end.Date : fullEnd;
                var days = (periodEnd - periodStart).Days + 1;
                var fullDays = (fullEnd - periodStart).Days + 1;

                var amount = basis switch
                {
                    RentBases.Daily => rate * days,
                    RentBases.Weekly => rate * days / 7m,
                    _ => days == fullDays ? rate : rate * days / fullDays
                };

                periods.Add(new Period(periodStart, periodEnd, VatDeterminationService.Round(amount)));
            }

            return periods;
        }

        public static List<Period> Periods(Lease lease) =>
            Periods(lease.RentBasis, lease.RentRate, lease.StartDate, lease.EndDate);

        /// <summary>The rent of the whole stay, before VAT.</summary>
        public static decimal Total(Lease lease) => Periods(lease).Sum(x => x.Amount);

        /// <summary>
        /// The stay's rent by calendar month, each day carrying an equal share of its
        /// billing period - what is earned in each month. Adds up exactly to the total.
        /// </summary>
        public static SortedDictionary<DateTime, decimal> ByCalendarMonth(Lease lease)
        {
            var months = new SortedDictionary<DateTime, decimal>();

            foreach (var period in Periods(lease))
            {
                var allocated = 0m;
                var cursor = period.Start;
                while (cursor <= period.End)
                {
                    var monthStart = new DateTime(cursor.Year, cursor.Month, 1);
                    var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                    var partEnd = monthEnd < period.End ? monthEnd : period.End;
                    var days = (partEnd - cursor).Days + 1;

                    // The last part takes what is left, so rounding never loses a fils.
                    var share = partEnd == period.End
                        ? period.Amount - allocated
                        : VatDeterminationService.Round(period.Amount * days / period.Days);

                    months[monthStart] = months.GetValueOrDefault(monthStart) + share;
                    allocated += share;
                    cursor = partEnd.AddDays(1);
                }
            }

            return months;
        }
    }
}
