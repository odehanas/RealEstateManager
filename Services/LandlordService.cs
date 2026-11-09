using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Landlords and the company's bank accounts. A landlord's name and TRN are also kept
    /// on each of their properties (settlements and reports group on them), so saving a
    /// landlord passes them on.
    /// </summary>
    public class LandlordService
    {
        private static readonly Regex IbanPattern = new(@"^[A-Z]{2}[0-9]{2}[A-Z0-9]{11,30}$", RegexOptions.Compiled);

        private readonly ApplicationDbContext _db;

        public LandlordService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  LANDLORDS
        // =====================================================================

        public Task<List<LandlordListItemViewModel>> GetLandlordsAsync() =>
            _db.Landlords.AsNoTracking()
                .OrderBy(x => x.Name)
                .Select(x => new LandlordListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Trn = x.Trn,
                    Phone = x.Phone,
                    Iban = x.Iban,
                    DefaultCollectionMode = x.DefaultCollectionMode,
                    IsActive = x.IsActive,
                    PropertyCount = _db.Properties.Count(p => p.LandlordId == x.Id)
                })
                .ToListAsync();

        public async Task<LandlordEditViewModel?> GetLandlordAsync(int id) =>
            await _db.Landlords.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new LandlordEditViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    Trn = x.Trn,
                    Email = x.Email,
                    Phone = x.Phone,
                    BankName = x.BankName,
                    AccountName = x.AccountName,
                    Iban = x.Iban,
                    DefaultCollectionMode = x.DefaultCollectionMode,
                    Notes = x.Notes,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync();

        /// <summary>Creates or updates a landlord. Returns its id, or null with the reasons in modelState.</summary>
        public async Task<int?> SaveLandlordAsync(ModelStateDictionary modelState, int companyId, LandlordEditViewModel model)
        {
            var name = model.Name?.Trim() ?? string.Empty;
            var iban = NormaliseIban(model.Iban);

            if (name.Length == 0)
                modelState.AddModelError(nameof(model.Name), "Enter the landlord's name.");
            else if (await _db.Landlords.AnyAsync(x => x.CompanyId == companyId && x.Name == name && x.Id != model.Id))
                modelState.AddModelError(nameof(model.Name), "A landlord with this name already exists.");

            if (iban != null && !IbanPattern.IsMatch(iban))
                modelState.AddModelError(nameof(model.Iban), "That IBAN does not look right. A UAE IBAN is AE followed by 21 digits.");

            if (!CollectionModes.All.Contains(model.DefaultCollectionMode))
                modelState.AddModelError(nameof(model.DefaultCollectionMode), "Choose how this landlord's rent is collected.");

            Landlord? landlord = null;
            if (model.Id != 0)
            {
                landlord = await _db.Landlords.FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyId);
                if (landlord == null)
                {
                    modelState.AddModelError(string.Empty, "The landlord could not be found.");
                    return null;
                }

                // Settlements and commission invoices already issued carry the landlord's name;
                // renaming would split the landlord's history in two.
                if (!string.Equals(landlord.Name, name, StringComparison.Ordinal) &&
                    (await _db.OwnerSettlements.AnyAsync(x => x.CompanyId == companyId && x.OwnerName == landlord.Name) ||
                     await _db.OwnerCommissionInvoices.AnyAsync(x => x.CompanyId == companyId && x.OwnerName == landlord.Name)))
                {
                    modelState.AddModelError(nameof(model.Name),
                        "This landlord already has settlements or commission invoices under this name, so the name cannot change.");
                }
            }

            if (!modelState.IsValid)
                return null;

            if (landlord == null)
            {
                landlord = new Landlord { CompanyId = companyId };
                _db.Landlords.Add(landlord);
            }

            landlord.Name = name;
            landlord.Trn = Clean(model.Trn);
            landlord.Email = Clean(model.Email);
            landlord.Phone = Clean(model.Phone);
            landlord.BankName = Clean(model.BankName);
            landlord.AccountName = Clean(model.AccountName);
            landlord.Iban = iban;
            landlord.DefaultCollectionMode = model.DefaultCollectionMode;
            landlord.Notes = Clean(model.Notes);
            landlord.IsActive = model.IsActive;

            // The properties carry the name and TRN that settlements and invoices use.
            if (landlord.Id != 0)
            {
                foreach (var property in await _db.Properties.Where(x => x.LandlordId == landlord.Id).ToListAsync())
                {
                    property.OwnerName = landlord.Name;
                    property.OwnerTrn = landlord.Trn;
                }
            }

            await _db.SaveChangesAsync();
            return landlord.Id;
        }

        /// <summary>
        /// Links a new managed property to the landlord its owner name gives, creating the
        /// landlord if there is none: for properties made by an import, the assistant or the
        /// sample data, which give the owner only as a name. Call before the property is saved.
        /// </summary>
        public static async Task LinkByOwnerNameAsync(ApplicationDbContext db, Property property)
        {
            var name = property.OwnerName?.Trim();
            if (property.OwnershipType != "Managed" || string.IsNullOrEmpty(name) || property.LandlordId.HasValue)
                return;
            if (name.Length > 200)
                name = name[..200];

            var landlord = await db.Landlords.IgnoreQueryFilters()
                .FirstOrDefaultAsync(x => x.CompanyId == property.CompanyId && x.Name == name);
            if (landlord == null)
            {
                landlord = new Landlord
                {
                    CompanyId = property.CompanyId,
                    Name = name,
                    Trn = string.IsNullOrWhiteSpace(property.OwnerTrn) ? null : property.OwnerTrn.Trim()
                };
                db.Landlords.Add(landlord);
                await db.SaveChangesAsync();
            }

            property.LandlordId = landlord.Id;
            property.OwnerName = landlord.Name;
            property.OwnerTrn = landlord.Trn ?? property.OwnerTrn;
            property.CollectionMode = landlord.DefaultCollectionMode;
        }

        // =====================================================================
        //  BANK ACCOUNTS
        // =====================================================================

        public Task<List<BankAccountListItemViewModel>> GetBankAccountsAsync() =>
            _db.BankAccounts.AsNoTracking()
                .OrderBy(x => x.Purpose).ThenBy(x => x.Name)
                .Select(x => new BankAccountListItemViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    BankName = x.BankName,
                    Iban = x.Iban,
                    AccountCode = x.Account!.Code,
                    AccountName = x.Account.Name,
                    Purpose = x.Purpose,
                    LandlordName = x.Landlord != null ? x.Landlord.Name : null,
                    IsActive = x.IsActive,
                    // Only posted entries: what the ledger says is in the bank.
                    Balance = _db.JournalEntryLines
                        .Where(l => l.AccountId == x.AccountId && l.JournalEntry!.IsPosted)
                        .Sum(l => (decimal?)(l.Debit - l.Credit)) ?? 0m
                })
                .ToListAsync();

        public async Task<BankAccountEditViewModel?> GetBankAccountAsync(int id) =>
            await _db.BankAccounts.AsNoTracking()
                .Where(x => x.Id == id)
                .Select(x => new BankAccountEditViewModel
                {
                    Id = x.Id,
                    Name = x.Name,
                    BankName = x.BankName,
                    Iban = x.Iban,
                    AccountId = x.AccountId,
                    Purpose = x.Purpose,
                    LandlordId = x.LandlordId,
                    IsActive = x.IsActive
                })
                .FirstOrDefaultAsync();

        /// <summary>The ledger accounts a bank account can be kept in: active, postable assets not tied to another bank account.</summary>
        public Task<List<Account>> GetLedgerChoicesAsync(int? exceptBankAccountId) =>
            _db.Accounts.AsNoTracking()
                .Where(x => x.AccountType == "Asset" && x.IsPostable && x.IsActive && !x.IsGroup && !x.IsPettyCashAccount &&
                            !_db.BankAccounts.Any(b => b.AccountId == x.Id && b.Id != (exceptBankAccountId ?? 0)))
                .OrderBy(x => x.Code)
                .ToListAsync();

        public async Task<int?> SaveBankAccountAsync(ModelStateDictionary modelState, int companyId, BankAccountEditViewModel model)
        {
            var name = model.Name?.Trim() ?? string.Empty;
            var iban = NormaliseIban(model.Iban);

            if (name.Length == 0)
                modelState.AddModelError(nameof(model.Name), "Give the bank account a name.");
            else if (await _db.BankAccounts.AnyAsync(x => x.CompanyId == companyId && x.Name == name && x.Id != model.Id))
                modelState.AddModelError(nameof(model.Name), "A bank account with this name already exists.");

            if (iban != null && !IbanPattern.IsMatch(iban))
                modelState.AddModelError(nameof(model.Iban), "That IBAN does not look right. A UAE IBAN is AE followed by 21 digits.");

            if (!BankAccountPurposes.All.Contains(model.Purpose))
                modelState.AddModelError(nameof(model.Purpose), "Choose what the account is for.");

            var choices = await GetLedgerChoicesAsync(model.Id == 0 ? null : model.Id);
            if (choices.All(x => x.Id != model.AccountId))
                modelState.AddModelError(nameof(model.AccountId), "Choose the ledger account this bank account is kept in. Each ledger account belongs to one bank account.");

            int? landlordId = null;
            if (model.Purpose == BankAccountPurposes.OwnersFunds && model.LandlordId.HasValue)
            {
                if (!await _db.Landlords.AnyAsync(x => x.Id == model.LandlordId.Value && x.CompanyId == companyId))
                    modelState.AddModelError(nameof(model.LandlordId), "Choose a landlord of this company.");
                landlordId = model.LandlordId;
            }

            BankAccount? bank = null;
            if (model.Id != 0)
            {
                bank = await _db.BankAccounts.FirstOrDefaultAsync(x => x.Id == model.Id && x.CompanyId == companyId);
                if (bank == null)
                {
                    modelState.AddModelError(string.Empty, "The bank account could not be found.");
                    return null;
                }
            }

            if (!modelState.IsValid)
                return null;

            if (bank == null)
            {
                bank = new BankAccount { CompanyId = companyId };
                _db.BankAccounts.Add(bank);
            }

            bank.Name = name;
            bank.BankName = Clean(model.BankName);
            bank.Iban = iban;
            bank.AccountId = model.AccountId;
            bank.Purpose = model.Purpose;
            bank.LandlordId = landlordId;
            bank.IsActive = model.IsActive;

            await _db.SaveChangesAsync();
            return bank.Id;
        }

        private static string? NormaliseIban(string? iban)
        {
            var clean = iban == null ? null : Regex.Replace(iban, @"\s+", string.Empty).ToUpperInvariant();
            return string.IsNullOrEmpty(clean) ? null : clean;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
