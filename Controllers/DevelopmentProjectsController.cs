using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// Operations ▸ Development Projects: buildings under construction, their budgets and
    /// the costs collected in them until completion.
    /// </summary>
    public class DevelopmentProjectsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly DevelopmentProjectService _projects;
        private readonly DevelopmentTransferService _transfers;

        public DevelopmentProjectsController(
            ApplicationDbContext db, DevelopmentProjectService projects, DevelopmentTransferService transfers)
        {
            _db = db;
            _projects = projects;
            _transfers = transfers;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            return View(await _projects.GetIndexAsync());
        }

        [HttpGet]
        public async Task<IActionResult> Details(int id)
        {
            var model = await _projects.GetDetailsAsync(id);
            if (model == null)
                return NotFound();

            model.Transfers = await _transfers.GetTransfersAsync(id);
            model.Units = await _transfers.GetUnitsAsync(id);

            ViewBag.CreditAccounts = (await _projects.GetCreditAccountsAsync())
                .Where(x => x.Id != model.Project.WipAccountId)
                .Select(x => new SelectListItem($"{x.Code} - {x.Name}", x.Id.ToString(), x.Code == "11100"))
                .ToList();

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            var model = await _projects.BuildFormAsync(null);
            await LoadFormListsAsync();
            return View("Edit", model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var model = await _projects.BuildFormAsync(id);
            await LoadFormListsAsync();
            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(DevelopmentProjectFormViewModel model)
        {
            try
            {
                var id = await _projects.SaveAsync(model);
                TempData["SuccessMessage"] = model.Id == 0 ? "Project created." : "Project saved.";
                return RedirectToAction(nameof(Details), new { id });
            }
            catch (InvalidOperationException ex)
            {
                ModelState.AddModelError(string.Empty, ex.Message);
                if (model.Budget.Count == 0)
                    model.Budget = DevelopmentCostCategories.All.Select(c => new DevelopmentBudgetInput { Category = c }).ToList();
                await LoadFormListsAsync();
                return View("Edit", model);
            }
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordCost(
            int id, DateTime costDate, string category, string description, decimal amount, int creditAccountId, string? reference)
        {
            try
            {
                var warning = await _projects.RecordCostAsync(id, costDate, category, description, amount, creditAccountId, reference);
                if (warning != null)
                    TempData["PostingWarning"] = $"The cost was saved but its journal is still a draft: {warning}";
                else
                    TempData["SuccessMessage"] = $"{DevelopmentCostCategories.Describe(category)} of {amount:N2} recorded and posted.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> StartConstruction(int id)
        {
            return await RunAsync(id, () => _projects.StartConstructionAsync(id), "Construction started.");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Complete(int id, DateTime completionDate, string? certificateNumber)
        {
            return await RunAsync(id, () => _projects.CompleteAsync(id, completionDate, certificateNumber),
                "Project marked complete. The completion date is now on the property record.");
        }

        /// <summary>Completion transfer: the cost leaves work in progress for the units' final accounts.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Transfer(int id, DateTime transferDate, int usefulLifeYears, Dictionary<int, string> uses)
        {
            try
            {
                var userName = User.FindFirst(AppClaimTypes.DisplayName)?.Value ?? User.FindFirst(AppClaimTypes.Username)?.Value;
                var transfer = await _transfers.TransferAsync(id, transferDate, usefulLifeYears, uses, userName);
                TempData["SuccessMessage"] =
                    $"{(transfer.LandCost + transfer.BuildingCost):N2} transferred and posted: land {transfer.LandCost:N2}, building {transfer.BuildingCost:N2}.";
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Cancel(int id)
        {
            return await RunAsync(id, () => _projects.CancelAsync(id), "Project cancelled.");
        }

        private async Task<IActionResult> RunAsync(int id, Func<Task> action, string success)
        {
            try
            {
                await action();
                TempData["SuccessMessage"] = success;
            }
            catch (InvalidOperationException ex)
            {
                TempData["ErrorMessage"] = ex.Message;
            }

            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task LoadFormListsAsync()
        {
            ViewBag.Properties = await _db.Properties.AsNoTracking()
                .Where(x => x.OwnershipType == "Owned")
                .OrderBy(x => x.Code)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString()))
                .ToListAsync();

            var wip = await _projects.GetWipAccountsAsync();
            ViewBag.WipAccounts = wip.Select(x => new SelectListItem($"{x.Code} - {x.Name}", x.Id.ToString())).ToList();

            // Default account per intended use, so the form can switch it when the use changes.
            ViewBag.DefaultWipAccounts = DevelopmentIntendedUses.All.ToDictionary(
                use => use,
                use => wip.FirstOrDefault(a => a.Code == DevelopmentIntendedUses.DefaultWipAccountCode(use))?.Id ?? 0);

            ViewBag.Vendors = await _db.Vendors.AsNoTracking()
                .Where(x => x.IsActive)
                .OrderBy(x => x.Name)
                .Select(x => new SelectListItem(x.Code + " - " + x.Name, x.Id.ToString()))
                .ToListAsync();
        }
    }
}
