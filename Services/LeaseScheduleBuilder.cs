using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// What a new lease brings with it: its rent invoices for the whole term, and the
    /// monthly revenue recognition schedule. Shared by the Leases screen and the sample
    /// data builder, so a lease is set up the same way whichever of them creates it.
    /// The caller saves the changes.
    /// </summary>
    public static class LeaseScheduleBuilder
    {
        /// <summary>
        /// The rent invoices the lease will produce, without saving anything - so a
        /// contract awaiting approval can show exactly what will be billed.
        /// </summary>
        public static List<CustomerInvoice> PlanInvoices(int companyId, Lease lease)
        {
            var invoices = new List<CustomerInvoice>();
            var billingMonths = lease.BillingFrequencyMonths;
            if (billingMonths <= 0 || 12 % billingMonths != 0)
                return invoices;

            var invoicesPerYear = 12 / billingMonths;
            var baseAmountPerInvoice = lease.AnnualRent / invoicesPerYear;

            // Tax is computed from the lease's VAT category through the one service
            // that owns that decision, so the rate and the rounding are identical
            // everywhere tax is calculated.
            var vatAmountPerInvoice = VatDeterminationService.CalculateTax(baseAmountPerInvoice, lease.VatCategory);

            var invoiceDate = lease.StartDate;

            while (invoiceDate <= lease.EndDate)
            {
                // The rent period this invoice bills - printed on the e-invoice.
                var periodEnd = invoiceDate.AddMonths(billingMonths).AddDays(-1);
                if (periodEnd > lease.EndDate) periodEnd = lease.EndDate;

                invoices.Add(new CustomerInvoice
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    InvoiceNumber = $"INV-{lease.ContractNumber}-{invoiceDate:yyyyMM}",
                    InvoiceDate = invoiceDate,
                    DueDate = invoiceDate,
                    DateOfSupply = invoiceDate,
                    PeriodStart = invoiceDate,
                    PeriodEnd = periodEnd,
                    Subtotal = baseAmountPerInvoice,
                    VatAmount = vatAmountPerInvoice,
                    TotalAmount = baseAmountPerInvoice + vatAmountPerInvoice,
                    AmountPaid = 0m,
                    // Snapshot the treatment so a later change to the property or the
                    // lease can never rewrite an invoice that has already been issued.
                    VatCategory = lease.VatCategory,
                    VatRate = lease.VatRate,
                    Status = "Draft"
                });

                invoiceDate = invoiceDate.AddMonths(billingMonths);
            }

            return invoices;
        }

        public static void GenerateInvoices(ApplicationDbContext db, int companyId, Lease lease)
        {
            db.CustomerInvoices.AddRange(PlanInvoices(companyId, lease));
        }

        /// <summary>
        /// One schedule line per month of the lease. For an owned property it is the
        /// month's rent, released from unearned revenue into rental income. For a managed
        /// property the rent belongs to the landlord; only our commission is income, so
        /// the line is the commission share, moved from Due to Landlords into management
        /// fee revenue - which is how the revenue recognition posting reads it.
        /// </summary>
        public static void GenerateRevenueRecognitionSchedules(ApplicationDbContext db, int companyId, Lease lease, Property property)
        {
            var monthlyRent = property.OwnershipType == "Managed"
                ? Math.Round(lease.AnnualRent / 12m * property.CommissionPercentage / 100m, 2)
                : Math.Round(lease.AnnualRent / 12m, 2);
            var startMonth = new DateTime(lease.StartDate.Year, lease.StartDate.Month, 1);
            var endMonth = new DateTime(lease.EndDate.Year, lease.EndDate.Month, 1);

            var currentMonth = startMonth;

            while (currentMonth <= endMonth)
            {
                var recognitionDate = new DateTime(
                    currentMonth.Year,
                    currentMonth.Month,
                    DateTime.DaysInMonth(currentMonth.Year, currentMonth.Month));

                db.RevenueRecognitionSchedules.Add(new RevenueRecognitionSchedule
                {
                    CompanyId = companyId,
                    LeaseId = lease.Id,
                    RecognitionDate = recognitionDate,
                    Amount = monthlyRent,
                    Status = "Planned"
                });

                currentMonth = currentMonth.AddMonths(1);
            }
        }
    }
}
