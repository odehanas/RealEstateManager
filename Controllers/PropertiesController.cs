using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class PropertiesController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CodeGenerationService _codeGenerationService;
        private readonly MasterDataUniquenessService _uniquenessService;

        public PropertiesController(
            ApplicationDbContext db,
            CodeGenerationService codeGenerationService,
            MasterDataUniquenessService uniquenessService)
        {
            _db = db;
            _codeGenerationService = codeGenerationService;
            _uniquenessService = uniquenessService;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var properties = await _db.Properties
                .Include(x => x.Company)
                .OrderBy(x => x.Code)
                .ToListAsync();

            return View(properties);
        }

        [HttpGet]
        public IActionResult Create()
        {
            return View(new Property { OwnershipType = "Owned" });
        }

        /// <summary>
        /// The code a new property would receive for the chosen ownership and type.
        /// Preview only - nothing is reserved until the property is saved.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> PreviewCode(string? ownershipType, string? propertyType)
        {
            if (string.IsNullOrWhiteSpace(propertyType))
                return Json(new { code = "Select ownership and type" });

            var code = await _codeGenerationService.PreviewPropertyCodeAsync(ownershipType, propertyType);
            return Json(new { code });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Property model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);

            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is generated, never typed, so the sequence cannot be broken.
            // Any value posted from the form is ignored.
            ModelState.Remove(nameof(model.Code));

            if (string.IsNullOrWhiteSpace(model.PropertyType))
            {
                ModelState.AddModelError(nameof(model.PropertyType),
                    "Select the property type - it determines the property code.");
            }

            await _uniquenessService.ValidatePropertyAsync(ModelState, companyId, model);
            await ValidateTitleDeedAsync(companyId, model, null);

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            // The registration fields bind straight onto the new record; this tidies them.
            model.CopyRegistrationFrom(model);

            model.Code = await _codeGenerationService.GeneratePropertyCodeAsync(
                model.OwnershipType,
                model.PropertyType);

            model.CompanyId = companyId;
            model.IsActive = true;

            _db.Properties.Add(model);
            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (property == null)
            {
                return NotFound();
            }

            return View(property);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Property model)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var property = await _db.Properties.FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (property == null)
            {
                return NotFound();
            }

            var company = await _db.Companies.FirstOrDefaultAsync(x => x.Id == companyId);
            if (company == null)
            {
                ModelState.AddModelError(string.Empty, "No company record found.");
            }

            // The code is issued by the system and fixed once units and contracts hang
            // off it, so it is never taken from the form.
            ModelState.Remove(nameof(model.Code));

            await _uniquenessService.ValidatePropertyAsync(ModelState, companyId, model, id);
            await ValidateTitleDeedAsync(companyId, model, id);

            if (!ModelState.IsValid)
            {
                model.Code = property.Code;
                model.CompanyId = property.CompanyId;
                return View(model);
            }

            property.Name = model.Name;
            property.Address = model.Address;
            property.PropertyType = model.PropertyType;
            property.OwnershipType = model.OwnershipType;
            property.VatCategory = string.IsNullOrWhiteSpace(model.VatCategory) ? property.VatCategory : model.VatCategory;
            property.OwnerName = model.OwnerName;
            property.OwnerTrn = string.IsNullOrWhiteSpace(model.OwnerTrn) ? null : model.OwnerTrn.Trim();
            property.AssetValue = model.AssetValue;
            property.CommissionPercentage = model.CommissionPercentage;
            property.IsActive = model.IsActive;
            property.CopyRegistrationFrom(model);

            await _db.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>One title deed is one property: the same deed number twice in an emirate is a duplicate.</summary>
        private async Task ValidateTitleDeedAsync(int companyId, Property model, int? excludeId)
        {
            var deed = model.TitleDeedNumber?.Trim();
            if (string.IsNullOrEmpty(deed))
                return;

            var emirate = string.IsNullOrWhiteSpace(model.Emirate) ? null : model.Emirate.Trim();
            var holder = await _db.Properties.AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.Id != (excludeId ?? 0) &&
                            x.TitleDeedNumber == deed && x.Emirate == emirate)
                .Select(x => new { x.Code, x.Name })
                .FirstOrDefaultAsync();

            if (holder != null)
            {
                ModelState.AddModelError(nameof(Property.TitleDeedNumber),
                    $"Title deed {deed} is already recorded on {holder.Code} - {holder.Name}.");
            }
        }

        // =====================================================================
        //  DETAILS AND DOCUMENTS
        // =====================================================================

        /// <summary>Everything recorded about a property: registration details, units and documents.</summary>
        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var property = await _db.Properties.AsNoTracking()
                .Include(x => x.Units)
                .FirstOrDefaultAsync(x => x.Id == id);
            if (property == null)
                return NotFound();

            // The files themselves are never loaded for the list.
            ViewBag.Documents = await _db.PropertyDocuments.AsNoTracking()
                .Where(x => x.PropertyId == id)
                .OrderBy(x => x.DocumentType).ThenByDescending(x => x.UploadedAt)
                .Select(x => new PropertyDocument
                {
                    Id = x.Id,
                    PropertyId = x.PropertyId,
                    UnitId = x.UnitId,
                    Unit = x.Unit,
                    DocumentType = x.DocumentType,
                    DocumentNumber = x.DocumentNumber,
                    Description = x.Description,
                    IssueDate = x.IssueDate,
                    ExpiryDate = x.ExpiryDate,
                    FileName = x.FileName,
                    ContentType = x.ContentType,
                    SizeBytes = x.SizeBytes,
                    UploadedAt = x.UploadedAt,
                    UploadedByName = x.UploadedByName
                })
                .ToListAsync();

            ViewBag.CanEdit = await HttpContext.RequestServices
                .GetRequiredService<PermissionService>()
                .HasPermissionAsync(User, AppPermissions.MasterDataManage);

            return View(property);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(PropertyDocument.MaxFileBytes + 1024 * 1024)]
        public async Task<IActionResult> UploadDocument(
            int id, IFormFile? file, string? documentType, string? documentNumber, string? description,
            DateTime? issueDate, DateTime? expiryDate, int? unitId)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var property = await _db.Properties.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id && x.CompanyId == companyId);
            if (property == null)
                return NotFound();

            string? error = null;
            var extension = Path.GetExtension(file?.FileName ?? string.Empty).ToLowerInvariant();

            if (file == null || file.Length == 0)
                error = "Choose the file to upload.";
            else if (file.Length > PropertyDocument.MaxFileBytes)
                error = $"The file is {file.Length / 1024 / 1024.0:N1} MB. The limit is {PropertyDocument.MaxFileBytes / 1024 / 1024} MB - scan at a lower resolution or save as PDF.";
            else if (!PropertyDocument.AllowedExtensions.Contains(extension))
                error = "Only PDF, JPG and PNG files can be uploaded.";
            else if (string.IsNullOrWhiteSpace(documentType) || !PropertyDocument.DocumentTypes.Contains(documentType))
                error = "Choose the document type.";
            else if (unitId.HasValue && !await _db.Units.AnyAsync(x => x.Id == unitId.Value && x.PropertyId == id))
                error = "The unit chosen does not belong to this property.";

            if (error != null)
            {
                TempData["ErrorMessage"] = error;
                return RedirectToAction(nameof(Details), new { id });
            }

            await using var stream = new MemoryStream();
            await file!.CopyToAsync(stream);

            _db.PropertyDocuments.Add(new PropertyDocument
            {
                CompanyId = companyId,
                PropertyId = id,
                UnitId = unitId,
                DocumentType = documentType!,
                DocumentNumber = string.IsNullOrWhiteSpace(documentNumber) ? null : documentNumber.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                IssueDate = issueDate?.Date,
                ExpiryDate = expiryDate?.Date,
                FileName = Path.GetFileName(file.FileName),
                ContentType = extension == ".pdf" ? "application/pdf" : extension == ".png" ? "image/png" : "image/jpeg",
                SizeBytes = file.Length,
                Content = stream.ToArray(),
                UploadedAt = DateTime.UtcNow,
                UploadedByName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value
            });
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{documentType} uploaded.";
            return RedirectToAction(nameof(Details), new { id });
        }

        /// <summary>Opens a stored document. A read action, open to anyone who may view the property.</summary>
        [HttpGet]
        public async Task<IActionResult> Document(int id)
        {
            var document = await _db.PropertyDocuments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            if (document == null)
                return NotFound();

            // Inline, so a PDF or image opens in the browser tab rather than downloading.
            Response.Headers.ContentDisposition = $"inline; filename=\"{document.FileName.Replace("\"", "")}\"";
            return File(document.Content, document.ContentType);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteDocument(int id)
        {
            var document = await _db.PropertyDocuments.FirstOrDefaultAsync(x => x.Id == id);
            if (document == null)
                return NotFound();

            var propertyId = document.PropertyId;
            _db.PropertyDocuments.Remove(document);
            await _db.SaveChangesAsync();

            TempData["SuccessMessage"] = $"{document.DocumentType} \"{document.FileName}\" deleted.";
            return RedirectToAction(nameof(Details), new { id = propertyId });
        }
    }
}
