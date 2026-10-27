using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;
using RealEstateManager.Services.Assistant;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Documents kept with tenants (Emirates ID, passport...) and contracts (the
    /// attested contract...), and the Document Expiry control page. An Emirates ID or
    /// passport is read on upload and its number and expiry fill the tenant record.
    /// </summary>
    public class RecordDocumentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly PermissionService _permissions;
        private readonly IdDocumentScanService _scanner;

        public RecordDocumentsController(ApplicationDbContext db, PermissionService permissions, IdDocumentScanService scanner)
        {
            _db = db;
            _permissions = permissions;
            _scanner = scanner;
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(RecordDocument.MaxFileBytes + 1024 * 1024)]
        public async Task<IActionResult> Upload(
            string entityType, int entityId, IFormFile? file, string? documentType, string? documentNumber,
            DateTime? issueDate, DateTime? expiryDate, bool readIt, string? returnUrl)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var back = BackTo(entityType, entityId, returnUrl);
            if (!await MayManageAsync(entityType))
                return Forbid();
            if (!await ExistsAsync(entityType, entityId))
                return NotFound();

            var extension = Path.GetExtension(file?.FileName ?? string.Empty).ToLowerInvariant();
            string? error = null;
            if (file == null || file.Length == 0)
                error = "Choose the file to upload.";
            else if (file.Length > RecordDocument.MaxFileBytes)
                error = $"The file is {file.Length / 1024 / 1024.0:N1} MB. The limit is {RecordDocument.MaxFileBytes / 1024 / 1024} MB - scan at a lower resolution or save as PDF.";
            else if (!RecordDocument.AllowedExtensions.Contains(extension))
                error = "Only PDF, JPG and PNG files can be uploaded.";
            else if (string.IsNullOrWhiteSpace(documentType) || !RecordDocumentTypes.For(entityType).Contains(documentType))
                error = "Choose the document type.";
            if (error != null)
            {
                TempData["DocumentError"] = error;
                return Redirect(back);
            }

            await using var stream = new MemoryStream();
            await file!.CopyToAsync(stream);
            var content = stream.ToArray();
            var mediaType = extension == ".pdf" ? "application/pdf" : extension == ".png" ? "image/png" : "image/jpeg";

            var document = new RecordDocument
            {
                CompanyId = companyId,
                EntityType = entityType,
                EntityId = entityId,
                DocumentType = documentType!,
                DocumentNumber = Clean(documentNumber),
                IssueDate = issueDate?.Date,
                ExpiryDate = expiryDate?.Date,
                FileName = Path.GetFileName(file.FileName),
                ContentType = mediaType,
                SizeBytes = file.Length,
                Content = content,
                UploadedAt = DateTime.UtcNow,
                UploadedByName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.Identity?.Name
            };

            var messages = new List<string> { $"{documentType} uploaded." };

            // An Emirates ID or passport is read, and fills what was left empty.
            if (readIt && entityType == RecordDocumentEntities.Tenant && RecordDocumentTypes.IsReadable(documentType!))
            {
                var (read, readError) = await _scanner.ReadAsync(content, mediaType, document.FileName);
                if (read == null)
                {
                    messages.Add(readError!);
                }
                else
                {
                    var number = documentType == RecordDocumentTypes.EmiratesId ? read.FormattedIdNumber : read.PassportNumber?.Trim();
                    document.DocumentNumber ??= number;
                    document.ExpiryDate ??= read.Expiry;
                    document.IssueDate ??= read.Issued;
                    messages.Add(await ApplyToTenantAsync(entityId, documentType!, document, read));
                }
            }
            else if (entityType == RecordDocumentEntities.Tenant && RecordDocumentTypes.IsReadable(documentType!))
            {
                messages.Add(await ApplyToTenantAsync(entityId, documentType!, document, null));
            }

            _db.RecordDocuments.Add(document);
            await _db.SaveChangesAsync();

            TempData["DocumentSuccess"] = string.Join(" ", messages.Where(m => !string.IsNullOrWhiteSpace(m)));
            return Redirect(back);
        }

        /// <summary>
        /// Copies the document's number and expiry to the tenant: the number only when the
        /// tenant has none (a different one is reported, never overwritten), the expiry
        /// always, since the newest document is the one that counts.
        /// </summary>
        private async Task<string> ApplyToTenantAsync(int tenantId, string documentType, RecordDocument document, IdDocumentScanService.IdDocument? read)
        {
            var tenant = await _db.Tenants.FirstAsync(x => x.Id == tenantId);
            var notes = new List<string>();
            var isId = documentType == RecordDocumentTypes.EmiratesId;
            var current = isId ? tenant.IdNumber : tenant.PassportNumber;
            var number = document.DocumentNumber;

            if (!string.IsNullOrWhiteSpace(number))
            {
                if (string.IsNullOrWhiteSpace(current))
                {
                    var holder = isId
                        ? await _db.Tenants.Where(x => x.Id != tenantId && x.IdNumber == number).Select(x => x.Code + " " + x.FullName).FirstOrDefaultAsync()
                        : await _db.Tenants.Where(x => x.Id != tenantId && x.PassportNumber == number).Select(x => x.Code + " " + x.FullName).FirstOrDefaultAsync();
                    if (holder != null)
                        notes.Add($"Number {number} is already on {holder}, so it was not copied to this tenant.");
                    else
                    {
                        if (isId) tenant.IdNumber = number; else tenant.PassportNumber = number;
                        notes.Add($"{documentType} number {number} filled in.");
                    }
                }
                else if (!string.Equals(Digits(current), Digits(number), StringComparison.OrdinalIgnoreCase))
                {
                    notes.Add($"Check: the document reads {number}, but the tenant record has {current}.");
                }
            }

            if (document.ExpiryDate.HasValue)
            {
                if (isId) tenant.IdExpiryDate = document.ExpiryDate; else tenant.PassportExpiryDate = document.ExpiryDate;
                notes.Add($"Expiry {document.ExpiryDate:dd/MM/yyyy} recorded" +
                          (document.ExpiryDate < DateTime.Today ? " - the document has EXPIRED." : "."));
            }

            if (read != null && string.IsNullOrWhiteSpace(tenant.Nationality) && !string.IsNullOrWhiteSpace(read.Nationality))
            {
                tenant.Nationality = read.Nationality.Trim();
                notes.Add($"Nationality {tenant.Nationality} filled in.");
            }

            if (read != null && !string.IsNullOrWhiteSpace(read.NameEnglish) &&
                !string.Equals(Simplify(read.NameEnglish), Simplify(tenant.FullName), StringComparison.OrdinalIgnoreCase))
                notes.Add($"The name on the document reads \"{read.NameEnglish.Trim()}\"; check it matches the tenant.");

            return string.Join(" ", notes);
        }

        [HttpGet]
        public async Task<IActionResult> Open(int id)
        {
            var document = await _db.RecordDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (document == null)
                return NotFound();
            Response.Headers["Content-Disposition"] = $"inline; filename=\"{document.FileName.Replace("\"", string.Empty)}\"";
            Response.Headers["X-Content-Type-Options"] = "nosniff";
            return File(document.Content, document.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id, string? returnUrl)
        {
            var document = await _db.RecordDocuments.FirstOrDefaultAsync(x => x.Id == id);
            if (document == null)
                return NotFound();
            if (!await MayManageAsync(document.EntityType))
                return Forbid();

            _db.RecordDocuments.Remove(document);
            await _db.SaveChangesAsync();
            TempData["DocumentSuccess"] = $"{document.DocumentType} removed.";
            return Redirect(BackTo(document.EntityType, document.EntityId, returnUrl));
        }

        /// <summary>The control page: documents expired or expiring soon, and contracts missing their attested copy.</summary>
        [HttpGet]
        public async Task<IActionResult> Expiry(int days = 60)
        {
            days = Math.Clamp(days, 7, 365);
            var limit = DateTime.Today.AddDays(days);
            var rows = new List<ExpiryRow>();

            var tenants = await _db.Tenants.AsNoTracking()
                .Where(x => x.IsActive && ((x.IdExpiryDate != null && x.IdExpiryDate <= limit) || (x.PassportExpiryDate != null && x.PassportExpiryDate <= limit)))
                .Select(x => new { x.Id, x.Code, x.FullName, x.IdNumber, x.IdExpiryDate, x.PassportNumber, x.PassportExpiryDate })
                .ToListAsync();
            foreach (var t in tenants)
            {
                if (t.IdExpiryDate <= limit)
                    rows.Add(new ExpiryRow("Tenant", $"{t.Code} {t.FullName}", RecordDocumentTypes.EmiratesId, t.IdNumber, t.IdExpiryDate!.Value, $"/Tenants/Edit/{t.Id}"));
                if (t.PassportExpiryDate <= limit)
                    rows.Add(new ExpiryRow("Tenant", $"{t.Code} {t.FullName}", RecordDocumentTypes.Passport, t.PassportNumber, t.PassportExpiryDate!.Value, $"/Tenants/Edit/{t.Id}"));
            }

            // Other tenant and contract documents with an expiry date (EID and passport come from the tenant above).
            var documents = await _db.RecordDocuments.AsNoTracking()
                .Where(x => x.ExpiryDate != null && x.ExpiryDate <= limit &&
                            x.DocumentType != RecordDocumentTypes.EmiratesId && x.DocumentType != RecordDocumentTypes.Passport)
                .Select(x => new { x.EntityType, x.EntityId, x.DocumentType, x.DocumentNumber, x.ExpiryDate })
                .ToListAsync();
            foreach (var d in documents)
            {
                var name = d.EntityType == RecordDocumentEntities.Lease
                    ? await _db.Leases.Where(x => x.Id == d.EntityId).Select(x => x.ContractNumber + " " + x.Tenant!.FullName).FirstOrDefaultAsync()
                    : await _db.Tenants.Where(x => x.Id == d.EntityId).Select(x => x.Code + " " + x.FullName).FirstOrDefaultAsync();
                rows.Add(new ExpiryRow(d.EntityType == RecordDocumentEntities.Lease ? "Contract" : "Tenant", name ?? "-", d.DocumentType, d.DocumentNumber,
                    d.ExpiryDate!.Value, d.EntityType == RecordDocumentEntities.Lease ? $"/Leases/Edit/{d.EntityId}" : $"/Tenants/Edit/{d.EntityId}"));
            }

            var propertyDocuments = await _db.PropertyDocuments.AsNoTracking()
                .Where(x => x.ExpiryDate != null && x.ExpiryDate <= limit)
                .Select(x => new { x.PropertyId, Property = x.Property!.Code + " " + x.Property.Name, x.DocumentType, x.DocumentNumber, x.ExpiryDate })
                .ToListAsync();
            rows.AddRange(propertyDocuments.Select(p => new ExpiryRow("Property", p.Property, p.DocumentType, p.DocumentNumber, p.ExpiryDate!.Value, $"/Properties/Details/{p.PropertyId}")));

            // Running contracts with no attested copy on file.
            var attested = _db.RecordDocuments.Where(x => x.EntityType == RecordDocumentEntities.Lease && x.DocumentType == RecordDocumentTypes.AttestedContract)
                .Select(x => x.EntityId);
            ViewBag.MissingAttestation = await _db.Leases.AsNoTracking()
                .Where(x => x.Status == LeaseStatuses.Active && !attested.Contains(x.Id))
                .OrderBy(x => x.ContractNumber)
                .Select(x => new ExpiryRow("Contract", x.ContractNumber + " " + x.Tenant!.FullName, RecordDocumentTypes.AttestedContract, null, x.StartDate, "/Leases/Edit/" + x.Id))
                .ToListAsync();
            ViewBag.Days = days;
            ViewBag.MissingIds = await _db.Tenants.AsNoTracking()
                .Where(x => x.IsActive && x.IdExpiryDate == null && x.Leases.Any(l => l.Status == LeaseStatuses.Active))
                .OrderBy(x => x.Code)
                .Select(x => new ExpiryRow("Tenant", x.Code + " " + x.FullName, RecordDocumentTypes.EmiratesId, x.IdNumber, DateTime.MinValue, "/Tenants/Edit/" + x.Id))
                .ToListAsync();

            return View(rows.OrderBy(x => x.ExpiryDate).ToList());
        }

        public sealed record ExpiryRow(string Kind, string Name, string Document, string? Number, DateTime ExpiryDate, string Link)
        {
            public int DaysLeft => (ExpiryDate.Date - DateTime.Today).Days;
        }

        private async Task<bool> MayManageAsync(string entityType) =>
            entityType == RecordDocumentEntities.Lease
                ? await _permissions.HasAnyPermissionAsync(User, AppPermissions.LeasesManage, AppPermissions.MasterDataManage)
                : entityType == RecordDocumentEntities.Tenant && await _permissions.HasPermissionAsync(User, AppPermissions.MasterDataManage);

        private async Task<bool> ExistsAsync(string entityType, int entityId) =>
            entityType == RecordDocumentEntities.Lease
                ? await _db.Leases.AnyAsync(x => x.Id == entityId)
                : entityType == RecordDocumentEntities.Tenant && await _db.Tenants.AnyAsync(x => x.Id == entityId);

        private string BackTo(string? entityType, int entityId, string? returnUrl) =>
            !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl
            : entityType == RecordDocumentEntities.Lease ? $"/Leases/Edit/{entityId}" : $"/Tenants/Edit/{entityId}";

        private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        private static string Digits(string? value) => new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
        private static string Simplify(string value) => new string(value.Where(char.IsLetter).ToArray());
    }
}
