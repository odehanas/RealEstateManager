using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// A contract's journey from draft to active.
    ///
    /// A new contract is a Draft: it can be corrected freely and has no financial effect.
    /// It takes effect - its rent invoices and income schedule are created, its unit is
    /// marked occupied and its terms are locked - in one of two ways:
    ///
    ///   - the company does not require approval: its creator activates it;
    ///   - the company requires approval: it is submitted, and a second person with the
    ///     Approve Contracts permission approves it. Returned, it goes back to Draft.
    ///
    /// While it waits, the unit is held: no other contract for the same unit and dates
    /// can be submitted or activated.
    /// </summary>
    public class LeaseWorkflowService : IApprovalHandler
    {
        private readonly ApplicationDbContext _db;
        private readonly ApprovalService _approvals;

        public LeaseWorkflowService(ApplicationDbContext db, ApprovalService approvals)
        {
            _db = db;
            _approvals = approvals;
        }

        public string DocumentType => ApprovalDocumentTypes.Lease;

        public string DocumentUrl(int documentId) => $"/Leases/Edit/{documentId}";

        public async Task<bool> ApprovalRequiredAsync()
        {
            var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync();
            return company?.RequireContractApproval == true;
        }

        /// <summary>
        /// Why the unit cannot take this contract: another contract that is active or
        /// awaiting approval overlaps its dates. Drafts do not hold a unit.
        /// </summary>
        public async Task<string?> CheckUnitFreeAsync(int leaseId, int unitId, DateTime startDate, DateTime endDate)
        {
            var clash = await _db.Leases.AsNoTracking()
                .Where(x => x.Id != leaseId &&
                            x.UnitId == unitId &&
                            (x.Status == LeaseStatuses.Active || x.Status == LeaseStatuses.PendingApproval) &&
                            startDate <= x.EndDate &&
                            endDate >= x.StartDate)
                .Select(x => new { x.ContractNumber, x.Status })
                .FirstOrDefaultAsync();

            return clash == null
                ? null
                : $"The unit is already taken for these dates by contract {clash.ContractNumber} " +
                  $"({LeaseStatuses.Describe(clash.Status).ToLowerInvariant()}).";
        }

        /// <summary>Puts a draft into effect at once - only when the company does not require approval.</summary>
        public async Task<(bool Success, string Message)> ActivateAsync(int leaseId)
        {
            if (await ApprovalRequiredAsync())
                return (false, "This company requires contract approval. Submit the contract for approval instead.");

            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == leaseId);
            if (lease == null)
                return (false, "The contract was not found.");

            if (lease.Status != LeaseStatuses.Draft)
                return (false, $"Only a draft can be activated; this contract is {LeaseStatuses.Describe(lease.Status).ToLowerInvariant()}.");

            var clash = await CheckUnitFreeAsync(lease.Id, lease.UnitId, lease.StartDate, lease.EndDate);
            if (clash != null)
                return (false, clash);

            await using var transaction = await _db.Database.BeginTransactionAsync();
            await PutIntoEffectAsync(lease);
            await _db.SaveChangesAsync();
            await transaction.CommitAsync();

            return (true, $"Contract {lease.ContractNumber} is active. Its rent invoices and income schedule are created.");
        }

        public async Task<(bool Success, string Message)> SubmitAsync(int leaseId, int userId, string userName)
        {
            if (!await ApprovalRequiredAsync())
                return (false, "This company does not require contract approval. Activate the contract instead.");

            var lease = await _db.Leases
                .Include(x => x.Tenant)
                .Include(x => x.Unit).ThenInclude(x => x!.Property)
                .FirstOrDefaultAsync(x => x.Id == leaseId);

            if (lease == null)
                return (false, "The contract was not found.");

            if (lease.Status != LeaseStatuses.Draft)
                return (false, $"Only a draft can be submitted; this contract is {LeaseStatuses.Describe(lease.Status).ToLowerInvariant()}.");

            var clash = await CheckUnitFreeAsync(lease.Id, lease.UnitId, lease.StartDate, lease.EndDate);
            if (clash != null)
                return (false, clash);

            await using var transaction = await _db.Database.BeginTransactionAsync();

            lease.Status = LeaseStatuses.PendingApproval;

            // Held for this contract while it waits, so it is not let twice.
            if (lease.Unit != null && lease.Unit.Status == "Vacant")
                lease.Unit.Status = "Reserved";

            await _db.SaveChangesAsync();

            var summary = $"{lease.Tenant?.FullName} - {lease.Unit?.Property?.Code}/{lease.Unit?.UnitNumber} - " +
                          $"{lease.AnnualRent:N2} a year, {lease.StartDate:dd/MM/yyyy} to {lease.EndDate:dd/MM/yyyy}";

            await _approvals.SubmitAsync(lease.CompanyId, DocumentType, lease.Id, lease.ContractNumber, summary,
                lease.CreatedByAppUserId, userId, userName);

            await transaction.CommitAsync();

            return (true, $"Contract {lease.ContractNumber} is submitted for approval.");
        }

        /// <summary>Removes a draft that will not go ahead. It has no invoices or journals.</summary>
        public async Task<(bool Success, string Message)> DeleteDraftAsync(int leaseId)
        {
            var lease = await _db.Leases.FirstOrDefaultAsync(x => x.Id == leaseId);
            if (lease == null)
                return (false, "The contract was not found.");

            if (lease.Status != LeaseStatuses.Draft)
                return (false, "Only a draft contract can be deleted.");

            if (await _db.CustomerInvoices.AnyAsync(x => x.LeaseId == leaseId))
                return (false, "This contract already has invoices, so it cannot be deleted.");

            _db.Leases.Remove(lease);
            await _db.SaveChangesAsync();
            return (true, $"Draft contract {lease.ContractNumber} is deleted.");
        }

        // =====================================================================
        //  APPROVAL HANDLER
        // =====================================================================

        public async Task<string?> CheckCanApproveAsync(int documentId)
        {
            var lease = await _db.Leases.AsNoTracking().FirstOrDefaultAsync(x => x.Id == documentId);
            if (lease == null)
                return "The contract no longer exists.";

            if (lease.Status != LeaseStatuses.PendingApproval)
                return $"The contract is {LeaseStatuses.Describe(lease.Status).ToLowerInvariant()}, not awaiting approval.";

            return await CheckUnitFreeAsync(lease.Id, lease.UnitId, lease.StartDate, lease.EndDate);
        }

        public async Task OnApprovedAsync(int documentId)
        {
            var lease = await _db.Leases.FirstAsync(x => x.Id == documentId);
            await PutIntoEffectAsync(lease);
        }

        public async Task OnReturnedAsync(int documentId)
        {
            var lease = await _db.Leases.Include(x => x.Unit).FirstAsync(x => x.Id == documentId);
            lease.Status = LeaseStatuses.Draft;

            // Release the hold unless another contract is now using the unit.
            if (lease.Unit != null && lease.Unit.Status == "Reserved" &&
                !await _db.Leases.AnyAsync(x => x.Id != lease.Id && x.UnitId == lease.UnitId &&
                                                (x.Status == LeaseStatuses.Active || x.Status == LeaseStatuses.PendingApproval)))
            {
                lease.Unit.Status = "Vacant";
            }
        }

        /// <summary>
        /// The moment a contract takes effect: active, unit occupied, every rent invoice and
        /// the income schedule created. The caller saves.
        /// </summary>
        private async Task PutIntoEffectAsync(Lease lease)
        {
            var unit = await _db.Units.Include(x => x.Property).FirstAsync(x => x.Id == lease.UnitId);

            lease.Status = LeaseStatuses.Active;
            unit.Status = "Occupied";

            LeaseScheduleBuilder.GenerateInvoices(_db, lease.CompanyId, lease);
            LeaseScheduleBuilder.GenerateRevenueRecognitionSchedules(_db, lease.CompanyId, lease, unit.Property!);
        }
    }
}
