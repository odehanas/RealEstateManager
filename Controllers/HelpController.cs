using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;

namespace RealEstateManager.Controllers
{
    [Authorize]
    public class HelpController : Controller
    {
        [HttpGet]
        public IActionResult Index()
        {
            var model = new HelpPageViewModel
            {
                Sections =
                [
                    new HelpSectionViewModel
                    {
                        Id = "getting-started",
                        Title = "Getting Started",
                        Summary = "Basic steps for a new customer company after registration.",
                        Steps =
                        [
                            "Sign in with your company admin account created during registration.",
                            "Review the 15-day trial status from the Subscription page.",
                            "Confirm your company settings, fiscal year, and chart of accounts setup.",
                            "Create users and assign rights only for the tasks each user should perform."
                        ]
                    },
                    new HelpSectionViewModel
                    {
                        Id = "daily-operations",
                        Title = "Daily Operations",
                        Summary = "Common usage flow for leases, accounting, and reporting.",
                        Steps =
                        [
                            "Set up properties, units, tenants, and vendors from Master Data.",
                            "Create lease contracts and related billing transactions.",
                            "Record receipts, vendor bills, vendor payments, fixed assets, and prepayments from Accounts.",
                            "Review Trial Balance, Profit and Loss, Balance Sheet, and Tenant Statements from Reports."
                        ]
                    },
                    new HelpSectionViewModel
                    {
                        Id = "security-access",
                        Title = "Security And Access",
                        Summary = "How permissions and audit tracking work in the system.",
                        Steps =
                        [
                            "System admin manages users, permissions, audit logs, and subscription activation across all companies.",
                            "Company admins should give users only the permissions they need, such as leases, accounting, or reports.",
                            "If access is denied, review the assigned permissions or ask the system admin to update rights.",
                            "Audit Logs help track who opened or processed actions inside the application."
                        ]
                    },
                    new HelpSectionViewModel
                    {
                        Id = "subscription-support",
                        Title = "Subscription And Support",
                        Summary = "How trial, activation, help, and feedback work.",
                        Steps =
                        [
                            "A new company starts with a 15-day trial automatically.",
                            "When you receive an activation code, open Subscription, enter the code, and apply it.",
                            "Use the Feedback page to suggest improvements or report usability issues for future releases.",
                            "Use this Help page as the standard guide until an AI assistant is introduced later."
                        ]
                    }
                ]
            };

            return View(model);
        }
    }
}
