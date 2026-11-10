using System.Reflection;
using Microsoft.EntityFrameworkCore;
using RealEstateManager.Domain;

namespace RealEstateManager.Services.Assistant
{
    /// <summary>What the assistant knows about the application itself: the help topics, the data model, and who it is talking to.</summary>
    public static class AssistantKnowledgeTools
    {
        /// <summary>Tables that are never described or queried: sign-ins, permissions, subscriptions, the assistant's own log.</summary>
        public static readonly HashSet<string> HiddenEntities = new(StringComparer.OrdinalIgnoreCase)
        {
            "AppUser", "AppUserCompany", "AppUserPermission", "TenantPortalUser", "EmployeePortalUser",
            "CompanySubscription", "ActivationCode", "SubscriptionActivationCode", "AuditLog", "SchemaScriptHistory",
            "AssistantSession", "AssistantMessage", "AssistantPendingAction", "UserFeedback", "Company", "SavedReport",
            "WhatsAppConnection"
        };

        public static IEnumerable<AssistantTool> All()
        {
            yield return new AssistantTool
            {
                Name = "search_help",
                Description = "Search the user manual for how to do something in RealEstateManager, what a screen is for, or an accounting rule. Use for every how-to question. Also returns the manual topic of the screen the user is on (thisScreen). Read a topic in full with get_help_topic before explaining it.",
                Parameters = Schema.Object(("query", Schema.Str("Two or three keywords, e.g. 'owner settlement', 'clear pdc' or 'close period'"), true)),
                Run = (ctx, args) =>
                {
                    var help = ctx.Get<HelpCatalogService>();
                    var results = help.Search(Args.Required(args, "query")).Take(6).Select(x => new
                    {
                        key = x.Topic.Key,
                        title = x.Topic.Title,
                        menu = x.Topic.Path,
                        summary = x.Topic.Summary,
                        excerpt = x.Excerpt
                    }).ToList();

                    // The topic the Help button of the user's screen opens: usually the answer
                    // to "what do I do here", whatever words the question used.
                    var parts = (ctx.Screen ?? string.Empty).Split('/', 2);
                    var screenTopic = parts.Length == 2 ? help.ForScreen(parts[0], parts[1]) : null;

                    return Task.FromResult<object?>(new
                    {
                        results,
                        thisScreen = screenTopic == null ? null : new { key = screenTopic.Key, title = screenTopic.Title, summary = screenTopic.Summary },
                        note = results.Count == 0
                            ? "No topic has those words. Do not tell the user the manual has nothing: pick the nearest title from allTopics and read it with get_help_topic."
                            : null,
                        // Every topic there is, when the words found nothing, so one can be chosen by title.
                        allTopics = results.Count == 0 ? help.AllTopics.Select(x => new { key = x.Key, title = x.Title }).ToList() : null
                    });
                }
            };

            yield return new AssistantTool
            {
                Name = "list_help_topics",
                Description = "Every topic of the user manual, by group, with its key and one-line summary. Use it when search_help does not find what you expect, or to see what the manual covers.",
                Run = (ctx, args) =>
                {
                    var help = ctx.Get<HelpCatalogService>();
                    return Task.FromResult<object?>(new
                    {
                        groups = help.Groups.Select(g => new
                        {
                            group = g.Title,
                            topics = g.Topics.Select(t => new { key = t.Key, title = t.Title, summary = t.Summary }).ToList()
                        }).ToList()
                    });
                }
            };

            yield return new AssistantTool
            {
                Name = "get_help_topic",
                Description = "Read one user-manual topic in full, by the key search_help returned.",
                Parameters = Schema.Object(("key", Schema.Str("Topic key"), true)),
                Run = (ctx, args) =>
                {
                    var help = ctx.Get<HelpCatalogService>();
                    var key = Args.Required(args, "key");
                    var topic = help.GetTopic(key);
                    if (topic == null)
                        return Task.FromResult<object?>(new { error = $"No topic '{key}'." });

                    // Long enough for the longest topics (invoicing, owner settlements) to arrive
                    // whole: a topic cut off half way is how the assistant came to say the manual
                    // did not cover something it does.
                    var text = help.GetPlainText(topic.Key);
                    if (text.Length > 20000) text = text[..20000] + " …";
                    return Task.FromResult<object?>(new { title = topic.Title, menu = topic.Path, text });
                }
            };

            yield return new AssistantTool
            {
                Name = "get_current_context",
                Description = "Who the user is, their company, currency, permissions, today's date, the screen they are on, and the open accounting periods.",
                Run = async (ctx, args) =>
                {
                    var permissions = ctx.User.IsInRole("SystemAdmin")
                        ? new List<string> { "SystemAdmin (all)" }
                        : (await ctx.Get<PermissionService>().GetUserPermissionsAsync(ctx.User)).OrderBy(x => x).ToList();
                    var company = await ctx.Db.Companies.AsNoTracking().Where(x => x.Id == ctx.CompanyId)
                        .Select(x => new { x.Name, x.LegalName, x.BaseCurrency, x.TrnNumber }).FirstAsync();
                    var openPeriods = await ctx.Db.AccountingPeriods.AsNoTracking().Where(x => !x.IsClosed)
                        .OrderBy(x => x.StartDate).Take(24)
                        .Select(x => new { x.Name, start = x.StartDate, end = x.EndDate }).ToListAsync();

                    return new
                    {
                        user = ctx.UserName,
                        company = company.Name,
                        legalName = company.LegalName,
                        currency = company.BaseCurrency,
                        vatRegistered = !string.IsNullOrWhiteSpace(company.TrnNumber),
                        today = DateTime.Today.ToString("yyyy-MM-dd"),
                        screen = ctx.Screen,
                        permissions,
                        openPeriods
                    };
                }
            };

            yield return new AssistantTool
            {
                Name = "describe_data_model",
                Description = "List the records the system keeps (entities), or one entity's fields, relations and allowed status values. Use before query_records.",
                Parameters = Schema.Object(("entity", Schema.Str("Entity name, e.g. Lease. Leave empty to list all."), false)),
                Run = (ctx, args) =>
                {
                    var name = Args.Str(args, "entity");
                    var types = ctx.Db.Model.GetEntityTypes()
                        .Where(x => !x.IsOwned() && !HiddenEntities.Contains(x.ClrType.Name))
                        .OrderBy(x => x.ClrType.Name)
                        .ToList();

                    if (name == null)
                        return Task.FromResult<object?>(new
                        {
                            entities = types.Select(x => x.ClrType.Name).ToList(),
                            statusValues = StatusConstants()
                        });

                    var type = types.FirstOrDefault(x => string.Equals(x.ClrType.Name, name, StringComparison.OrdinalIgnoreCase));
                    if (type == null)
                        return Task.FromResult<object?>(new { error = $"No entity '{name}'." });

                    return Task.FromResult<object?>(new
                    {
                        entity = type.ClrType.Name,
                        fields = type.GetProperties()
                            .Where(p => !AssistantQueryTools.IsHiddenField(p.Name))
                            .Select(p => new { name = p.Name, type = Nullable.GetUnderlyingType(p.ClrType)?.Name ?? p.ClrType.Name, key = p.IsPrimaryKey() ? true : (bool?)null })
                            .ToList(),
                        relations = type.GetNavigations()
                            .Where(n => !HiddenEntities.Contains(n.TargetEntityType.ClrType.Name))
                            .Select(n => new { name = n.Name, to = n.TargetEntityType.ClrType.Name, many = n.IsCollection })
                            .ToList(),
                        queryable = AssistantQueryTools.IsQueryable(type.ClrType.Name),
                        statusValues = StatusConstants()
                            .Where(kv => kv.Key.StartsWith(type.ClrType.Name, StringComparison.OrdinalIgnoreCase))
                            .ToDictionary(kv => kv.Key, kv => kv.Value)
                    });
                }
            };
        }

        private static Dictionary<string, List<string>>? _statusConstants;

        /// <summary>Every "...Statuses" / "...Types" constants class in Domain, e.g. LeaseStatuses: Draft, Active.</summary>
        public static Dictionary<string, List<string>> StatusConstants() =>
            _statusConstants ??= typeof(Lease).Assembly.GetTypes()
                .Where(t => t.IsAbstract && t.IsSealed && t.Namespace == typeof(Lease).Namespace &&
                            (t.Name.EndsWith("Statuses") || t.Name.EndsWith("Types") || t.Name.EndsWith("Methods")))
                .ToDictionary(
                    t => t.Name,
                    t => t.GetFields(BindingFlags.Public | BindingFlags.Static)
                        .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                        .Select(f => (string)f.GetRawConstantValue()!)
                        .ToList())
                .Where(kv => kv.Value.Count > 0)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
    }
}
