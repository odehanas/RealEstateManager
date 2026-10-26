using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RealEstateManager.Models;
using RealEstateManager.Services;

namespace RealEstateManager.Controllers
{
    /// <summary>
    /// The Help Center, single help topics, the panel that opens beside any screen, and
    /// the full printable manual. All four read the same topic files through
    /// HelpCatalogService.
    /// </summary>
    [Authorize]
    public class HelpController : Controller
    {
        private readonly HelpCatalogService _help;

        public HelpController(HelpCatalogService help)
        {
            _help = help;
        }

        /// <summary>Every topic, grouped the way the menus are, with search.</summary>
        [HttpGet]
        public IActionResult Index(string? q)
        {
            var model = new HelpCenterViewModel
            {
                Query = q?.Trim(),
                Groups = _help.Groups,
                Results = _help.Search(q)
            };

            return View(model);
        }

        /// <summary>One topic as a full page.</summary>
        [HttpGet]
        public IActionResult Topic(string id)
        {
            var topic = _help.GetTopic(id);
            if (topic == null)
                return NotFound();

            var (previous, next) = _help.Neighbours(topic.Key);

            return View(new HelpTopicViewModel
            {
                Topic = topic,
                Content = _help.GetContent(topic.Key, HelpLinkMode.Page),
                Group = _help.Groups.First(x => x.Key == topic.GroupKey),
                Previous = previous,
                Next = next
            });
        }

        /// <summary>
        /// The help panel's contents. Returns a fragment, not a page - the panel loads it
        /// into itself with fetch, and loads linked topics the same way, so following a
        /// cross-reference never takes the user away from the screen they are working on.
        /// </summary>
        [HttpGet]
        public IActionResult Panel(string? id)
        {
            var topic = _help.GetTopic(id);

            if (topic == null)
                return PartialView("_PanelIndex", _help.Groups);

            return PartialView("_Panel", new HelpTopicViewModel
            {
                Topic = topic,
                Content = _help.GetContent(topic.Key, HelpLinkMode.Page),
                Group = _help.Groups.First(x => x.Key == topic.GroupKey)
            });
        }

        /// <summary>
        /// The whole manual as one printable document, in catalog order, with a cover and
        /// contents. Open to anyone: it is documentation of the product, carries no company
        /// data, and being anonymous lets the PDF be produced straight from this page. The
        /// previous static manual was publicly readable for the same reason.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Manual()
        {
            var model = new HelpManualViewModel
            {
                Groups = _help.Groups,
                Content = _help.AllTopics.ToDictionary(
                    x => x.Key,
                    x => _help.GetContent(x.Key, HelpLinkMode.Anchor))
            };

            return View(model);
        }

        /// <summary>
        /// The Training Workbook: practice transactions for new users, step by step with
        /// the result to expect. Public for the same reason as the manual, so its PDF can
        /// be produced straight from this page.
        /// </summary>
        [AllowAnonymous]
        [HttpGet]
        public IActionResult Training() => View();

        /// <summary>
        /// For owners: the controls the system enforces, the ones to adopt as policy, and
        /// plain answers to the security questions owners ask. Signed-in owners only.
        /// </summary>
        [HttpGet]
        public IActionResult Controls() => View();
    }
}
