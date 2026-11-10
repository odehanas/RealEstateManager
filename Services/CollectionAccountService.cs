using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Where a tenant's money lands, by who holds the rent of the property
    /// (<see cref="CollectionModes"/>). It is the only thing the arrangement changes: the
    /// debit of a receipt, or of a cheque when it clears. Everything before that - the
    /// invoice, the cheque register, deposit, collection and a returned cheque - is the same
    /// whoever holds the money.
    ///
    ///   Company       Dr the company's cash account (Account Mappings: Default Cash)
    ///   OwnersFunds   Dr the owners' funds bank account: the landlord's own one if they have
    ///                 one, else the one for all landlords, else 11120 Bank - Owners' Funds
    ///   Owner         Dr Due to Landlords: the landlord banked it, so we owe them that much
    ///                 less, and once it passes their share the balance is our commission
    ///                 owed by them
    /// </summary>
    public class CollectionAccountService
    {
        public const string OwnersFundsAccountCode = "11120";

        private readonly ApplicationDbContext _db;

        public CollectionAccountService(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <summary>Who holds money paid against an invoice: a cheque charge is always the company's own income.</summary>
        public static string ModeFor(Property property, bool isCompanyCharge) =>
            isCompanyCharge ? CollectionModes.Company : property.EffectiveCollectionMode;

        public async Task<Account> ReceivingAccountAsync(int companyId, Property property, string? mode)
        {
            var settings = await _db.CompanyAccountSettings.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId)
                ?? throw new InvalidOperationException("Account Mappings are not set up for this company.");

            switch (CollectionModes.Clean(mode))
            {
                case CollectionModes.Owner:
                    return await MappedAsync(companyId, settings.DueToLandlordsAccountId, "Due To Landlords");

                case CollectionModes.OwnersFunds:
                    return await OwnersFundsAccountAsync(companyId, property.LandlordId)
                        ?? throw new InvalidOperationException(
                            $"{property.Name} collects rent into an owners' funds bank account, but none is set up. " +
                            "Add one under System Setup > Bank Accounts, or change how this property's rent is collected.");

                default:
                    return await MappedAsync(companyId, settings.DefaultCashAccountId, "Default Cash");
            }
        }

        /// <summary>The account a landlord's money is kept in under the owners' funds arrangement, or null when there is none.</summary>
        public async Task<Account?> OwnersFundsAccountAsync(int companyId, int? landlordId)
        {
            var bank = await _db.BankAccounts.AsNoTracking().IgnoreQueryFilters()
                .Include(x => x.Account)
                .Where(x => x.CompanyId == companyId && x.IsActive && x.Purpose == BankAccountPurposes.OwnersFunds &&
                            (x.LandlordId == null || x.LandlordId == landlordId) &&
                            x.Account!.IsActive && x.Account.IsPostable)
                // The landlord's own account first, then the shared one.
                .OrderBy(x => x.LandlordId == null ? 1 : 0).ThenBy(x => x.Id)
                .FirstOrDefaultAsync();

            if (bank != null)
                return bank.Account;

            return await _db.Accounts.AsNoTracking().IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == companyId && x.Code == OwnersFundsAccountCode && x.IsActive && x.IsPostable);
        }

        private async Task<Account> MappedAsync(int companyId, int? accountId, string mappingName)
        {
            if (!accountId.HasValue)
                throw new InvalidOperationException($"Account mapping '{mappingName}' is not configured.");

            return await _db.Accounts.AsNoTracking().IgnoreQueryFilters()
                       .FirstOrDefaultAsync(x => x.Id == accountId.Value && x.CompanyId == companyId)
                   ?? throw new InvalidOperationException($"Account mapping '{mappingName}' points to an account that does not exist.");
        }
    }
}
