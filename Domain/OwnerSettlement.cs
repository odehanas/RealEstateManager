namespace RealEstateManager.Domain
{
    /// <summary>
    /// A payment to the landlord of a managed property: the rent we collected on the
    /// owner's behalf, less our commission, paid over in full or in part.
    ///
    /// The figures behind the payment (rent, commission, owner's share, what was paid
    /// before) are stored as they stood on the day, so the settlement advice can be
    /// reprinted later exactly as it was handed to the owner.
    ///
    /// The journal is  Dr Due to Landlords / Cr the bank or cash account paid from.
    /// A settlement counts only once its journal is posted.
    /// </summary>
    public class OwnerSettlement
    {
        public int Id { get; set; }

        public int CompanyId { get; set; }
        public Company? Company { get; set; }

        /// <summary>OS-yyyy-0001, per company.</summary>
        public string SettlementNumber { get; set; } = string.Empty;

        /// <summary>The landlord, as named on the managed property records.</summary>
        public string OwnerName { get; set; } = string.Empty;

        /// <summary>The day the money left: the journal date.</summary>
        public DateTime SettlementDate { get; set; }

        /// <summary>The cut-off the statement was drawn up to.</summary>
        public DateTime AsOfDate { get; set; }

        /// <summary>Rent billed to tenants up to the cut-off, before VAT, after credit notes.</summary>
        public decimal GrossRent { get; set; }

        /// <summary>Our commission on that rent.</summary>
        public decimal Commission { get; set; }

        /// <summary>5% VAT on the commission, charged to the owner on our commission tax invoices.</summary>
        public decimal CommissionVat { get; set; }

        /// <summary>Rent less commission and its VAT: what belongs to the owner.</summary>
        public decimal OwnerShare { get; set; }

        /// <summary>The owner's share of rent actually collected from tenants.</summary>
        public decimal CollectedOwnerShare { get; set; }

        /// <summary>Paid to the owner before this settlement.</summary>
        public decimal PreviouslySettled { get; set; }

        /// <summary>Paid now. May be less than the balance - a partial settlement.</summary>
        public decimal AmountPaid { get; set; }

        public string PaymentMethod { get; set; } = "Bank Transfer";

        public int PaidFromAccountId { get; set; }
        public Account? PaidFromAccount { get; set; }

        /// <summary>Cheque or transfer reference.</summary>
        public string? ReferenceNumber { get; set; }

        public string? Notes { get; set; }

        public int? JournalEntryId { get; set; }
        public JournalEntry? JournalEntry { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public int? CreatedByAppUserId { get; set; }
        public string? CreatedByName { get; set; }

        /// <summary>What the owner was still owed after this payment.</summary>
        public decimal BalanceAfter => OwnerShare - PreviouslySettled - AmountPaid;

        public bool IsPosted => JournalEntry?.IsPosted == true;
    }
}
