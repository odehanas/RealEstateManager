using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    public class CustomerReceiptsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly CustomerSettlementService _settlements;

        public CustomerReceiptsController(ApplicationDbContext db, CustomerSettlementService settlements)
        {
            _db = db;
            _settlements = settlements;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var receipts = await _db.CustomerReceipts
                .Include(x => x.CustomerSettlement)
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Unit)
                            .ThenInclude(x => x!.Property)
                .Include(x => x.CustomerInvoice)
                    .ThenInclude(x => x!.Lease)
                        .ThenInclude(x => x!.Tenant)
                .OrderByDescending(x => x.ReceiptDate)
                .ThenByDescending(x => x.Id)
                .ToListAsync();

            return View(receipts);
        }

        /// <summary>
        /// Receive Payment. The tenant is chosen first; their open invoices are then
        /// listed. Opened from an invoice, it starts on that invoice's tenant with the
        /// invoice chosen and its balance as the amount.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> Create(int? tenantId, int? invoiceId)
        {
            var form = new CustomerSettlementCreateViewModel();

            if (invoiceId.HasValue)
            {
                var invoice = await _db.CustomerInvoices
                    .Include(x => x.Lease)
                    .FirstOrDefaultAsync(x => x.Id == invoiceId.Value);
                if (invoice?.Lease != null)
                {
                    tenantId = invoice.Lease.TenantId;
                    form.InvoiceIds.Add(invoice.Id);
                    form.Amount = invoice.TotalAmount - invoice.AmountPaid;
                }
            }

            form.TenantId = tenantId ?? 0;
            return View(await BuildPageAsync(form));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind(Prefix = "Form")] CustomerSettlementCreateViewModel form)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var plan = await _settlements.PlanAsync(companyId, form);
            foreach (var error in plan.Errors)
            {
                ModelState.AddModelError(error.Field.Length == 0 ? string.Empty : $"Form.{error.Field}", error.Message);
            }

            if (!ModelState.IsValid)
            {
                return View(await BuildPageAsync(form));
            }

            try
            {
                var result = await _settlements.CreateAsync(companyId, form, plan.Allocations, User.Identity?.Name);
                if (result.NotPostedReasons.Count > 0)
                    TempData["PostingWarning"] =
                        $"Receipt {result.Settlement.SettlementNumber} was saved as a draft but not posted: {string.Join(" ", result.NotPostedReasons)}";

                return RedirectToAction(nameof(Settlement), new { id = result.Settlement.Id });
            }
            catch (Exception ex)
            {
                // Show the reason on the form instead of a blank 500 page. The rollback
                // means nothing was saved, so the user can correct and retry safely.
                ModelState.AddModelError(string.Empty,
                    $"The receipt could not be posted, so nothing was saved. {ex.Message}");
                return View(await BuildPageAsync(form));
            }
        }

        /// <summary>The settlement document: the payment and the invoices it was matched to.</summary>
        [HttpGet]
        public async Task<IActionResult> Settlement(int id)
        {
            var settlement = await _settlements.LoadAsync(id);
            return settlement == null ? NotFound() : View(settlement);
        }

        private async Task<CustomerSettlementPageViewModel> BuildPageAsync(CustomerSettlementCreateViewModel form)
        {
            var companyId = User.GetRequiredCurrentCompanyId();
            var page = new CustomerSettlementPageViewModel
            {
                Form = form,
                Tenants = await _settlements.TenantOptionsAsync(),
                Currency = await _db.Companies.Where(x => x.Id == companyId).Select(x => x.BaseCurrency).FirstOrDefaultAsync() ?? "AED"
            };

            if (form.TenantId > 0)
            {
                page.Tenant = await _db.Tenants.FirstOrDefaultAsync(x => x.Id == form.TenantId);
                if (page.Tenant != null)
                    page.OpenInvoices = await _settlements.OpenInvoicesAsync(page.Tenant.Id);
            }

            return page;
        }
    }
}
