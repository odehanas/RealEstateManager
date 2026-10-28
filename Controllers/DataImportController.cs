using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Services;
using RealEstateManager.Services.Import;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Master Data ▸ Import Data: bringing a customer's records over from their old system.
    /// Download the template, fill it, upload it; the file is checked, and imported only when
    /// every row is right. An uploaded file waits on the server between the check and the
    /// import (named for its company and user, so nobody else can import it) and is deleted
    /// after a day.
    /// </summary>
    public class DataImportController : Controller
    {
        private const long MaxFileBytes = 10 * 1024 * 1024;
        private static readonly TimeSpan KeepFilesFor = TimeSpan.FromDays(1);

        private readonly MasterDataImportService _import;
        private readonly PermissionService _permissions;

        public DataImportController(MasterDataImportService import, PermissionService permissions)
        {
            _import = import;
            _permissions = permissions;
        }

        [HttpGet]
        public async Task<IActionResult> Index() => View(new DataImportViewModel { Sheets = await AllowedSheetsAsync() });

        /// <summary>The template, with only the sheets this user may import, in their language.</summary>
        [HttpGet]
        public async Task<IActionResult> Template()
        {
            var sheets = await AllowedSheetsAsync();
            if (sheets.Count == 0)
                return Forbid();

            return File(_import.BuildTemplate(sheets),
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "RealEstateManager-Import-Template.xlsx");
        }

        /// <summary>Checks an uploaded file and shows what would be imported, or what is wrong.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        [RequestSizeLimit(MaxFileBytes + 64 * 1024)]
        public async Task<IActionResult> Check(IFormFile? file)
        {
            var sheets = await AllowedSheetsAsync();
            var model = new DataImportViewModel { Sheets = sheets };

            if (file == null || file.Length == 0)
                model.Error = "Choose the filled template to upload.";
            else if (file.Length > MaxFileBytes)
                model.Error = "The file is larger than 10 MB. Split it into several files.";
            else if (!string.Equals(Path.GetExtension(file.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase))
                model.Error = "Upload the template as an Excel workbook (.xlsx).";
            if (model.Error != null)
                return View(nameof(Index), model);

            CleanUp();
            var token = Guid.NewGuid().ToString("N");
            var path = FilePath(token);
            await using (var saved = System.IO.File.Create(path))
                await file!.CopyToAsync(saved);

            model.Batch = await ReadAndCheckAsync(path, sheets);
            model.Token = model.Batch.IsValid ? token : null;
            model.FileName = Path.GetFileName(file!.FileName);
            if (!model.Batch.IsValid)
                System.IO.File.Delete(path);

            return View(nameof(Index), model);
        }

        /// <summary>Imports a file that passed the check, after checking it again as it is now.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Import(string token)
        {
            var sheets = await AllowedSheetsAsync();
            var path = FilePath(token);
            if (!IsToken(token) || !System.IO.File.Exists(path))
                return View(nameof(Index), new DataImportViewModel { Sheets = sheets, Error = "That upload has expired. Upload the file again." });

            // The records may have changed since the check: another user, or the same file twice.
            var batch = await ReadAndCheckAsync(path, sheets);
            if (!batch.IsValid)
            {
                System.IO.File.Delete(path);
                return View(nameof(Index), new DataImportViewModel { Sheets = sheets, Batch = batch });
            }

            MasterDataImportService.ImportResult result;
            try
            {
                result = await _import.ImportAsync(batch, User.GetRequiredCurrentCompanyId(), User.GetAppUserId());
            }
            catch (Exception ex) when (ex is InvalidOperationException or Microsoft.EntityFrameworkCore.DbUpdateException)
            {
                return View(nameof(Index), new DataImportViewModel
                {
                    Sheets = sheets,
                    Error = "Nothing was imported: " + (ex.InnerException?.Message ?? ex.Message)
                });
            }

            byte[] resultFile;
            await using (var original = System.IO.File.OpenRead(path))
                resultFile = _import.BuildResultFile(original, result);
            System.IO.File.Delete(path);
            await System.IO.File.WriteAllBytesAsync(FilePath(token, "result"), resultFile);

            return View(nameof(Index), new DataImportViewModel { Sheets = sheets, Result = result, Token = token });
        }

        /// <summary>The imported file with each record's new code beside it.</summary>
        [HttpGet]
        public IActionResult Result(string token)
        {
            var path = FilePath(token, "result");
            if (!IsToken(token) || !System.IO.File.Exists(path))
                return NotFound();

            return PhysicalFile(path, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                "RealEstateManager-Import-Result.xlsx");
        }

        // ---------------------------------------------------------------------

        private async Task<ImportBatch> ReadAndCheckAsync(string path, IReadOnlyCollection<ImportSheet> sheets)
        {
            ImportBatch batch;
            await using (var stream = System.IO.File.OpenRead(path))
                batch = _import.Read(stream, sheets);
            if (batch.Total > 0)
                await _import.CheckAsync(batch, User.GetRequiredCurrentCompanyId());
            return batch;
        }

        private async Task<IReadOnlyCollection<ImportSheet>> AllowedSheetsAsync()
        {
            var allowed = new List<ImportSheet>();
            foreach (var sheet in ImportSchema.Sheets)
                if (await _permissions.HasPermissionAsync(User, sheet.Permission))
                    allowed.Add(sheet);
            return allowed;
        }

        private static string Folder()
        {
            var folder = Path.Combine(Path.GetTempPath(), "rem-imports");
            Directory.CreateDirectory(folder);
            return folder;
        }

        private string FilePath(string token, string kind = "upload") =>
            Path.Combine(Folder(), $"{User.GetRequiredCurrentCompanyId()}-{User.GetAppUserId()}-{(IsToken(token) ? token : "x")}-{kind}.xlsx");

        private static bool IsToken(string? token) => token is { Length: 32 } && token.All(Uri.IsHexDigit);

        private static void CleanUp()
        {
            foreach (var file in Directory.EnumerateFiles(Folder(), "*.xlsx"))
            {
                try
                {
                    if (DateTime.UtcNow - System.IO.File.GetLastWriteTimeUtc(file) > KeepFilesFor)
                        System.IO.File.Delete(file);
                }
                catch (IOException) { /* in use; next time */ }
            }
        }
    }

    public class DataImportViewModel
    {
        public IReadOnlyCollection<ImportSheet> Sheets { get; set; } = Array.Empty<ImportSheet>();
        public ImportBatch? Batch { get; set; }
        public MasterDataImportService.ImportResult? Result { get; set; }
        public string? Token { get; set; }
        public string? FileName { get; set; }
        public string? Error { get; set; }
    }
}
