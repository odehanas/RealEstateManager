using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Generates the hierarchical master-data codes so users never type them and
    /// sequences cannot be broken:
    ///
    ///   Property   OWN-R-0001                  ownership + type + sequence
    ///   Unit       OWN-R-0001-U01              property code + unit sequence
    ///   Contract   OWN-R-0001-U01-C001         unit code + contract sequence
    ///
    /// Each level's sequence is scoped to its parent, so unit numbering restarts in
    /// every property and contract numbering restarts on every unit. A renewal on the
    /// same unit therefore becomes -C002, which makes the contract history of a unit
    /// readable from the codes alone.
    ///
    /// Sequences are derived from the highest existing code rather than from a counter
    /// table, so they survive deletions and cope with codes that were typed by hand
    /// before this was introduced.
    /// </summary>
    public class CodeGenerationService
    {
        private readonly ApplicationDbContext _db;

        /// <summary>Guards the retry loop if something unexpected keeps colliding.</summary>
        private const int MaxAttempts = 200;

        public CodeGenerationService(ApplicationDbContext db)
        {
            _db = db;
        }

        // =====================================================================
        //  PREFIXES
        // =====================================================================

        public static string OwnershipPrefix(string? ownershipType) => ownershipType switch
        {
            "Owned" => "OWN",
            "Managed" => "MNG",
            _ => "PRP"
        };

        public static string PropertyTypeLetter(string? propertyType) => propertyType switch
        {
            "Residential" => "R",
            "Commercial" => "C",
            "Warehouse" => "W",
            "MixedUse" => "M",
            _ => "X"
        };

        /// <summary>The fixed part of a property code, e.g. "OWN-R-".</summary>
        public static string BuildPropertyPrefix(string? ownershipType, string? propertyType)
            => $"{OwnershipPrefix(ownershipType)}-{PropertyTypeLetter(propertyType)}-";

        // =====================================================================
        //  GENERATION
        // =====================================================================

        /// <summary>
        /// Next free tenant code, e.g. TNT-00001. Five digits, widening past 99999.
        ///
        /// Tenants sit outside the property hierarchy - a tenant can hold contracts on
        /// several units across several properties - so the sequence is a single running
        /// number per company rather than being scoped to a parent.
        /// </summary>
        public async Task<string> GenerateTenantCodeAsync()
        {
            const string prefix = "TNT-";

            var existing = await _db.Tenants
                .AsNoTracking()
                .Where(x => x.Code.StartsWith(prefix))
                .Select(x => x.Code)
                .ToListAsync();

            var next = NextSequence(existing, prefix);

            for (var attempt = 0; attempt < MaxAttempts; attempt++, next++)
            {
                var candidate = $"{prefix}{Pad(next, 5)}";

                // The unique index is on (CompanyId, Code) and the query filter already
                // scopes to the current company, so this checks the right thing.
                if (!await _db.Tenants.AnyAsync(x => x.Code == candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                "Could not allocate a tenant code. Check for corrupt codes in the tenant list.");
        }

        /// <summary>
        /// Next free vendor code, e.g. VEN-0001. Four digits, widening past 9999.
        /// Like tenants, vendors sit outside the property hierarchy, so the sequence is
        /// a single running number per company.
        /// </summary>
        public async Task<string> GenerateVendorCodeAsync()
        {
            const string prefix = "VEN-";

            var existing = await _db.Vendors
                .AsNoTracking()
                .Where(x => x.Code.StartsWith(prefix))
                .Select(x => x.Code)
                .ToListAsync();

            var next = NextSequence(existing, prefix);

            for (var attempt = 0; attempt < MaxAttempts; attempt++, next++)
            {
                var candidate = $"{prefix}{Pad(next, 4)}";

                if (!await _db.Vendors.AnyAsync(x => x.Code == candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                "Could not allocate a vendor code. Check for corrupt codes in the vendor list.");
        }

        /// <summary>Next free property code for this ownership and type combination.</summary>
        public async Task<string> GeneratePropertyCodeAsync(string? ownershipType, string? propertyType)
        {
            var prefix = BuildPropertyPrefix(ownershipType, propertyType);

            var existing = await _db.Properties
                .AsNoTracking()
                .Where(x => x.Code.StartsWith(prefix))
                .Select(x => x.Code)
                .ToListAsync();

            var next = NextSequence(existing, prefix);

            for (var attempt = 0; attempt < MaxAttempts; attempt++, next++)
            {
                var candidate = $"{prefix}{next:D4}";
                if (!await _db.Properties.AnyAsync(x => x.Code == candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Could not allocate a property code for prefix {prefix}. Check for corrupt codes in the property list.");
        }

        /// <summary>
        /// Next free unit code within a property, e.g. OWN-R-0001-U01. Two digits, but
        /// widening automatically past 99 rather than failing on a large building.
        /// </summary>
        public async Task<string> GenerateUnitCodeAsync(int propertyId)
        {
            var propertyCode = await _db.Properties
                .AsNoTracking()
                .Where(x => x.Id == propertyId)
                .Select(x => x.Code)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("The selected property was not found.");

            var prefix = $"{propertyCode}-U";

            var existing = await _db.Units
                .AsNoTracking()
                .Where(x => x.PropertyId == propertyId && x.UnitNumber.StartsWith(prefix))
                .Select(x => x.UnitNumber)
                .ToListAsync();

            var next = NextSequence(existing, prefix);

            for (var attempt = 0; attempt < MaxAttempts; attempt++, next++)
            {
                var candidate = $"{prefix}{Pad(next, 2)}";
                if (!await _db.Units.AnyAsync(x => x.PropertyId == propertyId && x.UnitNumber == candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Could not allocate a unit code for property {propertyCode}.");
        }

        /// <summary>
        /// Contract code for a lease, e.g. OWN-R-0001-U01-C001-2026.
        ///
        /// The C number identifies the TENANCY on that unit and the year identifies the
        /// contract term within it. So:
        ///
        ///   Tenant A, 2026            OWN-R-0001-U01-C001-2026
        ///   Tenant A renews, 2027     OWN-R-0001-U01-C001-2027   same number, new year
        ///   Tenant B takes over, 2027 OWN-R-0001-U01-C002-2027   new tenancy, new number
        ///
        /// A renewal is recognised as the same tenant taking the same unit again - no
        /// extra field for the user to remember to set. If the same tenant somehow needs
        /// two contracts on one unit in one year, the second takes the next free C number
        /// so the code stays unique.
        /// </summary>
        public async Task<string> GenerateContractCodeAsync(int unitId, int tenantId, DateTime startDate)
        {
            var unitCode = await _db.Units
                .AsNoTracking()
                .Where(x => x.Id == unitId)
                .Select(x => x.UnitNumber)
                .FirstOrDefaultAsync()
                ?? throw new InvalidOperationException("The selected unit was not found.");

            var prefix = $"{unitCode}-C";
            var year = startDate.Year;

            var unitLeases = await _db.Leases
                .AsNoTracking()
                .Where(x => x.UnitId == unitId)
                .Select(x => new { x.TenantId, x.ContractNumber })
                .ToListAsync();

            // Has this tenant held this unit before? Then it is a renewal and keeps the
            // tenancy's existing number.
            var tenancySequence = unitLeases
                .Where(x => x.TenantId == tenantId)
                .Select(x => ParseContractSequence(x.ContractNumber, prefix))
                .Where(x => x.HasValue)
                .Select(x => x!.Value)
                .OrderBy(x => x)
                .FirstOrDefault();

            int sequence;

            if (tenancySequence > 0)
            {
                sequence = tenancySequence;
            }
            else
            {
                var highest = unitLeases
                    .Select(x => ParseContractSequence(x.ContractNumber, prefix))
                    .Where(x => x.HasValue)
                    .Select(x => x!.Value)
                    .DefaultIfEmpty(0)
                    .Max();

                sequence = highest + 1;
            }

            for (var attempt = 0; attempt < MaxAttempts; attempt++, sequence++)
            {
                var candidate = $"{prefix}{Pad(sequence, 3)}-{year}";
                if (!await _db.Leases.AnyAsync(x => x.ContractNumber == candidate))
                    return candidate;
            }

            throw new InvalidOperationException(
                $"Could not allocate a contract code for unit {unitCode}.");
        }

        /// <summary>
        /// Pulls the C number out of a contract code, ignoring the year that follows it.
        /// Returns null for legacy codes that do not fit the pattern, so hand-entered
        /// contract numbers never corrupt the sequence.
        /// </summary>
        private static int? ParseContractSequence(string contractNumber, string prefix)
        {
            if (string.IsNullOrEmpty(contractNumber) || !contractNumber.StartsWith(prefix))
                return null;

            var tail = contractNumber[prefix.Length..];

            // "001-2026" -> "001"; also tolerates a bare "001" from the earlier scheme.
            var separator = tail.IndexOf('-');
            var sequencePart = separator >= 0 ? tail[..separator] : tail;

            return int.TryParse(sequencePart, out var value) ? value : null;
        }

        /// <summary>
        /// Preview of the code a new property would receive. Used to show the user what
        /// they are about to get without reserving anything.
        /// </summary>
        public Task<string> PreviewPropertyCodeAsync(string? ownershipType, string? propertyType)
            => GeneratePropertyCodeAsync(ownershipType, propertyType);

        // =====================================================================
        //  INTERNALS
        // =====================================================================

        /// <summary>
        /// Highest numeric suffix among codes sharing a prefix, plus one. Codes whose
        /// suffix is not a plain number are ignored, so hand-entered legacy codes never
        /// break generation.
        /// </summary>
        private static int NextSequence(IEnumerable<string> existingCodes, string prefix)
        {
            var highest = 0;

            foreach (var code in existingCodes)
            {
                if (code.Length <= prefix.Length)
                    continue;

                var suffix = code[prefix.Length..];

                if (int.TryParse(suffix, out var value) && value > highest)
                    highest = value;
            }

            return highest + 1;
        }

        /// <summary>Zero-pads to a minimum width, widening naturally beyond it.</summary>
        private static string Pad(int value, int width) => value.ToString(new string('0', width));
    }
}
