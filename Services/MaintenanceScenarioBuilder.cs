using Microsoft.EntityFrameworkCore;
using RealEstateManager.Data;
using RealEstateManager.Domain;

namespace RealEstateManager.Services
{
    /// <summary>
    /// Fills a training company with a few months of maintenance work, so the maintenance
    /// manager, an engineer and the owner each have something real to look at: jobs from
    /// every source, at every stage, on time and late, rated well and badly, with materials
    /// on owned and managed properties, a make-ready, inspections and a planned programme.
    ///
    /// It needs a company that already has properties and tenants (the training sample
    /// data), and it runs once. Jobs are shared among the company's maintenance users; a
    /// company with none gets the same jobs unassigned.
    ///
    /// Run inside a scope set to the company (see SampleDataService), so every query here
    /// is already filtered to it.
    /// </summary>
    public sealed class MaintenanceScenarioBuilder
    {
        /// <summary>Marks what this builder made, so it is never loaded twice.</summary>
        public const string Marker = "Maintenance scenarios (sample)";

        private readonly ApplicationDbContext _db;

        public MaintenanceScenarioBuilder(ApplicationDbContext db)
        {
            _db = db;
        }

        private sealed record Place(int PropertyId, string PropertyCode, bool Managed, int? UnitId, int? TenantId, int? LeaseId);

        private sealed record Material(string Description, decimal Quantity, decimal UnitCost);

        private sealed class Scenario
        {
            public double DaysAgo { get; init; }
            public string Source { get; init; } = ServiceRequestSources.Portal;
            public string Priority { get; init; } = ServiceRequestPriorities.Normal;
            public string Category { get; init; } = "Other";
            public string Subject { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string Status { get; init; } = ServiceRequestStatuses.Closed;

            /// <summary>Hours from the request to the first response; null while nobody has responded.</summary>
            public double? ResponseHours { get; init; }

            /// <summary>Hours from the request to the work being done; null while it is open.</summary>
            public double? DoneHours { get; init; }
            public int? Rating { get; init; }
            public string? Feedback { get; init; }
            public string? Resolution { get; init; }
            public string? Purpose { get; init; }

            /// <summary>For planned work: the due date, in days from the day the job was opened.</summary>
            public int? DueInDays { get; init; }
            public int? Plan { get; init; }
            public bool Unassigned { get; init; }

            /// <summary>Work on the building itself rather than inside one unit.</summary>
            public bool CommonArea { get; init; }
            public Material[] Materials { get; init; } = Array.Empty<Material>();
            public (bool FromTenant, string Body, bool Internal)[] Comments { get; init; } = Array.Empty<(bool, string, bool)>();
        }

        public async Task<string> BuildAsync(int companyId, int? actingUserId)
        {
            if (await _db.MaintenancePlans.AnyAsync(x => x.CreatedByName == Marker))
                throw new InvalidOperationException("The maintenance scenarios have already been loaded into this company.");

            var today = DateTime.Today;
            var leases = await _db.Leases.AsNoTracking()
                .Where(x => x.Status == LeaseStatuses.Active && x.StartDate <= today && x.EndDate >= today && x.Unit!.Property!.IsActive)
                .OrderBy(x => x.Id)
                .Select(x => new Place(x.Unit!.PropertyId, x.Unit.Property!.Code, x.Unit.Property.OwnershipType == "Managed", x.UnitId, x.TenantId, x.Id))
                .ToListAsync();
            if (leases.Count == 0)
                throw new InvalidOperationException(
                    "This company has no tenant with a current contract. Load the sample data first, or add a property, a unit and an active contract.");

            // Owned and managed properties in turn, so the cost charts show both sides.
            var tenanted = Interleave(leases.Where(x => !x.Managed).ToList(), leases.Where(x => x.Managed).ToList());
            var buildings = tenanted.GroupBy(x => x.PropertyId).Select(g => g.First() with { UnitId = null, TenantId = null, LeaseId = null }).ToList();
            var vacant = await _db.Units.AsNoTracking()
                .Where(x => x.Status == "Vacant" && x.Property!.IsActive)
                .OrderBy(x => x.Id)
                .Select(x => new Place(x.PropertyId, x.Property!.Code, x.Property.OwnershipType == "Managed", x.Id, null, null))
                .FirstOrDefaultAsync();

            var technicians = await MaintenanceService.TechniciansAsync(_db, companyId);
            var raisedBy = actingUserId.HasValue
                ? await _db.AppUsers.IgnoreQueryFilters().Where(x => x.Id == actingUserId.Value).Select(x => x.DisplayName).FirstOrDefaultAsync()
                : null;

            // ---- The planned programme --------------------------------------------
            var planProperty = buildings[0];
            var plans = new[]
            {
                new MaintenancePlan { Name = "Quarterly AC service", Description = "Clean filters and coils, check gas pressure and drains on every split unit.", Category = "Air Conditioning", IntervalMonths = 3, LeadDays = 7, NextDueDate = today.AddDays(85) },
                new MaintenancePlan { Name = "Six-monthly fire alarm and extinguisher check", Description = "Test the panel, sounders and call points; check extinguisher pressure and tags.", Category = "Common Areas", Priority = ServiceRequestPriorities.High, IntervalMonths = 6, LeadDays = 10, NextDueDate = today.AddDays(160) },
                new MaintenancePlan { Name = "Yearly water tank cleaning", Description = "Drain, clean and disinfect the roof and ground tanks; keep the municipality certificate.", Category = "Cleaning", IntervalMonths = 12, LeadDays = 14, NextDueDate = today.AddDays(367) }
            };
            foreach (var plan in plans)
            {
                plan.CompanyId = companyId;
                plan.PropertyId = planProperty.PropertyId;
                plan.IsActive = true;
                plan.CreatedAt = DateTime.UtcNow;
                plan.CreatedByName = Marker;
                plan.AssignToAppUserId = technicians.Count > 0 ? technicians[0].Id : null;
                _db.MaintenancePlans.Add(plan);
            }
            await _db.SaveChangesAsync();

            // ---- The jobs, oldest first -------------------------------------------
            var scenarios = Scenarios();
            var year = today.Year;
            var numbers = new Dictionary<int, int>();
            var jobs = 0;
            var materialLines = 0;
            var tenantTurn = 0;
            var buildingTurn = 0;
            var technicianTurn = 0;

            foreach (var s in scenarios.OrderByDescending(x => x.DaysAgo))
            {
                var internalJob = ServiceRequestSources.IsInternal(s.Source);
                Place place =
                    s.Purpose == MaintenancePurposes.MakeReady && vacant != null ? vacant
                    : s.Plan.HasValue ? planProperty
                    : s.CommonArea ? buildings[buildingTurn++ % buildings.Count]
                    : tenanted[tenantTurn++ % tenanted.Count];

                var submittedLocal = today.AddDays(-Math.Floor(s.DaysAgo)).AddHours(9).AddHours(-(s.DaysAgo - Math.Floor(s.DaysAgo)) * 24);
                if (s.DaysAgo < 1)
                    submittedLocal = DateTime.Now.AddHours(-s.DaysAgo * 24);
                var submitted = submittedLocal.ToUniversalTime();

                var open = s.Status is ServiceRequestStatuses.Submitted or ServiceRequestStatuses.Acknowledged or ServiceRequestStatuses.InProgress;
                var done = s.Status is ServiceRequestStatuses.Resolved or ServiceRequestStatuses.Closed;
                int? technician = s.Unassigned || technicians.Count == 0 ? null : technicians[technicianTurn++ % technicians.Count].Id;
                var resolved = done && s.DoneHours.HasValue ? submitted.AddHours(s.DoneHours.Value) : (DateTime?)null;

                var job = new TenantServiceRequest
                {
                    CompanyId = companyId,
                    TenantId = internalJob ? null : place.TenantId,
                    LeaseId = internalJob ? null : place.LeaseId,
                    PropertyId = place.PropertyId,
                    UnitId = s.CommonArea || s.Plan.HasValue ? null : place.UnitId,
                    RequestNumber = await NextNumberAsync(companyId, submittedLocal.Year, numbers),
                    RequestType = ServiceRequestTypes.Maintenance,
                    Category = s.Category,
                    Subject = s.Subject,
                    Description = s.Description,
                    Priority = s.Priority,
                    PreferredVisitDate = s.DueInDays.HasValue ? submittedLocal.Date.AddDays(s.DueInDays.Value) : null,
                    Status = s.Status,
                    SubmittedAt = submitted,
                    AcknowledgedAt = s.ResponseHours.HasValue ? submitted.AddHours(s.ResponseHours.Value) : null,
                    WorkStartedAt = s.ResponseHours.HasValue && (done || s.Status == ServiceRequestStatuses.InProgress) ? submitted.AddHours(s.ResponseHours.Value + 1) : null,
                    ResolvedAt = resolved,
                    ClosedAt = s.Status == ServiceRequestStatuses.Closed && resolved.HasValue ? resolved.Value.AddDays(1) : null,
                    AssignedToAppUserId = technician,
                    ResolutionNotes = s.Resolution,
                    Source = s.Source,
                    Purpose = s.Purpose,
                    MaintenancePlanId = s.Plan.HasValue ? plans[s.Plan.Value].Id : null,
                    Rating = done ? s.Rating : null,
                    RatingFeedback = done && s.Rating.HasValue ? s.Feedback : null,
                    RatedAt = done && s.Rating.HasValue && resolved.HasValue ? resolved.Value.AddHours(6) : null
                };

                var hours = 0.5;
                foreach (var (fromTenant, body, isInternal) in s.Comments)
                {
                    job.Comments.Add(new TenantServiceRequestComment
                    {
                        AuthorType = fromTenant ? ServiceRequestAuthorTypes.Tenant : ServiceRequestAuthorTypes.Staff,
                        AuthorName = fromTenant ? "Tenant" : "Maintenance team",
                        AppUserId = fromTenant ? null : technician ?? actingUserId,
                        Body = body,
                        IsInternal = isInternal,
                        CreatedAt = submitted.AddHours((s.ResponseHours ?? 1) + hours)
                    });
                    hours += 2;
                }

                _db.TenantServiceRequests.Add(job);
                await _db.SaveChangesAsync();
                jobs++;

                // Bought for the job, on a receipt: recorded on the job without moving the
                // maintenance store, whose balance must stay what the ledger says.
                foreach (var m in s.Materials)
                {
                    _db.MaintenanceMaterials.Add(new MaintenanceMaterial
                    {
                        CompanyId = companyId,
                        TenantServiceRequestId = job.Id,
                        MaterialDate = (resolved ?? DateTime.UtcNow).ToLocalTime().Date,
                        Description = m.Description,
                        Quantity = m.Quantity,
                        UnitCost = m.UnitCost,
                        Amount = Math.Round(m.Quantity * m.UnitCost, 2),
                        Source = MaintenanceMaterialSources.Purchased,
                        Reference = $"Receipt S-{job.Id:0000}",
                        ChargedTo = place.Managed ? "Owner" : "Company",
                        CreatedAt = DateTime.UtcNow,
                        CreatedByName = raisedBy ?? Marker
                    });
                    materialLines++;
                }
                await _db.SaveChangesAsync();
            }

            var team = technicians.Count == 0
                ? "This company has no maintenance user, so every job is unassigned: add users with the Maintenance Manager and Maintenance Engineer roles, then assign the open jobs from Maintenance Jobs."
                : $"Shared among {technicians.Count} maintenance user(s): {string.Join(", ", technicians.Select(x => x.Name))}.";
            return $"Maintenance scenarios loaded: {jobs} jobs over the last five months, {materialLines} material lines and {plans.Length} planned maintenance entries. {team}";
        }

        private static List<Place> Interleave(List<Place> first, List<Place> second)
        {
            var result = new List<Place>();
            for (var i = 0; i < Math.Max(first.Count, second.Count); i++)
            {
                if (i < first.Count) result.Add(first[i]);
                if (i < second.Count) result.Add(second[i]);
            }
            return result;
        }

        /// <summary>The next REQ-year-0000 number of the company, continuing its own sequence.</summary>
        private async Task<string> NextNumberAsync(int companyId, int year, Dictionary<int, int> numbers)
        {
            if (!numbers.TryGetValue(year, out var highest))
            {
                var prefix = $"REQ-{year}-";
                var existing = await _db.TenantServiceRequests.IgnoreQueryFilters()
                    .Where(x => x.CompanyId == companyId && x.RequestNumber.StartsWith(prefix))
                    .Select(x => x.RequestNumber)
                    .ToListAsync();
                highest = existing.Select(x => int.TryParse(x[prefix.Length..], out var n) ? n : 0).DefaultIfEmpty(0).Max();
            }

            numbers[year] = ++highest;
            return $"REQ-{year}-{highest:0000}";
        }

        private static List<Scenario> Scenarios() => new()
        {
            // ---- History: done and closed, most on time, a few not -----------------
            new() { DaysAgo = 150, Source = ServiceRequestSources.Portal, Category = "Plumbing", Subject = "Kitchen tap dripping",
                    Description = "The kitchen mixer tap drips all night and the handle is loose.",
                    ResponseHours = 3, DoneHours = 30, Rating = 9, Feedback = "Quick and tidy. Thank you.",
                    Resolution = "Replaced the cartridge and tightened the handle.",
                    Materials = new[] { new Material("Mixer tap cartridge", 1, 45) },
                    Comments = new[] { (false, "Thank you for reporting this. Our plumber will visit tomorrow morning between 9 and 11.", false), (true, "That works, I will be at home.", false) } },

            new() { DaysAgo = 138, Source = ServiceRequestSources.Phone, Priority = ServiceRequestPriorities.High, Category = "Air Conditioning", Subject = "Bedroom AC not cooling",
                    Description = "The master bedroom split unit runs but blows warm air.",
                    ResponseHours = 2, DoneHours = 20, Rating = 10, Feedback = "Came the same day and fixed it in one visit.",
                    Resolution = "Low gas and a failed capacitor. Topped up the gas, replaced the capacitor, tested for leaks.",
                    Materials = new[] { new Material("Refrigerant gas R410A top-up", 1, 180), new Material("Fan capacitor", 1, 35) } },

            new() { DaysAgo = 125, Source = ServiceRequestSources.WhatsApp, Priority = ServiceRequestPriorities.Urgent, Category = "Plumbing", Subject = "Water leaking through the bathroom ceiling",
                    Description = "Water is coming through the ceiling above the bathroom door. Sent photos by WhatsApp.",
                    ResponseHours = 1, DoneHours = 10, Rating = 8, Feedback = "Fast response. The ceiling still needs painting.",
                    Resolution = "Burst PPR joint on the unit above. Isolated, cut out and re-welded. Ceiling tile to be replaced on a follow-up job.",
                    Materials = new[] { new Material("PPR pipe and fittings", 1, 220) },
                    Comments = new[] { (false, "We are on our way. Please switch off the water heater and keep clear of the area.", false), (false, "Source is the unit above - coordinate access with that tenant.", true) } },

            new() { DaysAgo = 118, Source = ServiceRequestSources.Portal, Category = "Electrical", Subject = "Living room socket not working",
                    Description = "The double socket behind the TV has no power. Other sockets are fine.",
                    ResponseHours = 20, DoneHours = 96, Rating = 7, Feedback = "Fixed, but it took four days.",
                    Resolution = "Loose neutral in the socket. Re-terminated and tested.",
                    Materials = new[] { new Material("13A double socket", 1, 28) } },

            new() { DaysAgo = 104, Source = ServiceRequestSources.Management, Category = "Common Areas", Subject = "Lobby and corridor lighting inspection",
                    Description = "Walk every floor after dark; list failed lamps and emergency lights.", Purpose = MaintenancePurposes.Inspection, CommonArea = true,
                    ResponseHours = 6, DoneHours = 48, Resolution = "Eleven lamps and two emergency fittings replaced. List filed.",
                    Materials = new[] { new Material("LED lamp 12W", 11, 9), new Material("Emergency light fitting", 2, 65) } },

            new() { DaysAgo = 97, Source = ServiceRequestSources.Portal, Priority = ServiceRequestPriorities.Low, Category = "Carpentry", Subject = "Wardrobe door hinge loose",
                    Description = "One wardrobe door in the second bedroom hangs lower than the other.",
                    ResponseHours = 30, DoneHours = 200, Rating = 8, Feedback = "No hurry needed and it was done properly.",
                    Resolution = "Replaced two hinges and aligned the doors.",
                    Materials = new[] { new Material("Soft-close hinge", 2, 15) } },

            new() { DaysAgo = 92, Source = ServiceRequestSources.Planned, Category = "Air Conditioning", Subject = "Quarterly AC service",
                    Description = "Clean filters and coils, check gas pressure and drains on every split unit.", Purpose = MaintenancePurposes.Preventive,
                    Plan = 0, DueInDays = 7, ResponseHours = 24, DoneHours = 120, Resolution = "All units serviced. Two drain lines cleared.",
                    Materials = new[] { new Material("AC filter", 4, 25), new Material("Coil cleaner", 2, 30) } },

            new() { DaysAgo = 83, Source = ServiceRequestSources.WhatsApp, Priority = ServiceRequestPriorities.High, Category = "Appliances", Subject = "Water heater not heating",
                    Description = "No hot water since yesterday. The heater light is on.",
                    ResponseHours = 12, DoneHours = 100, Rating = 4, Feedback = "Took four days and two visits. We had no hot water all that time.",
                    Resolution = "Heating element failed. First visit had the wrong part; replaced on the second visit.",
                    Materials = new[] { new Material("Water heater element 1500W", 1, 160) },
                    Comments = new[] { (true, "Any update? It has been two days.", false), (false, "The part arrives tomorrow morning. We apologise for the delay.", false) } },

            new() { DaysAgo = 75, Source = ServiceRequestSources.Portal, Category = "Pest Control", Subject = "Cockroaches in the kitchen",
                    Description = "Seeing cockroaches around the sink and under the fridge at night.",
                    ResponseHours = 5, DoneHours = 50, Rating = 9, Feedback = "No more insects after the treatment.",
                    Resolution = "Gel bait treatment in the kitchen and bathrooms. Follow-up in three weeks if needed.",
                    Materials = new[] { new Material("Gel bait treatment", 1, 150) } },

            new() { DaysAgo = 66, Source = ServiceRequestSources.Email, Category = "Electrical", Subject = "Request to add sockets on the balcony",
                    Description = "We would like two outdoor sockets added on the balcony.", Status = ServiceRequestStatuses.Rejected,
                    ResponseHours = 20, Resolution = "Declined: alterations to the electrical installation need the landlord's written approval and are not maintenance." },

            new() { DaysAgo = 58, Source = ServiceRequestSources.Management, Category = "Other", Subject = "Make unit ready for the next tenant",
                    Description = "Tenancy ended. Repaint, deep clean, service the AC and check every tap, socket and lock.", Purpose = MaintenancePurposes.MakeReady,
                    ResponseHours = 10, DoneHours = 110, Resolution = "Painted throughout, deep cleaned, AC serviced, two taps and one lock replaced. Ready to let.",
                    Materials = new[] { new Material("Emulsion paint 18L", 4, 85), new Material("Deep cleaning", 1, 250), new Material("Door lock set", 1, 70) } },

            new() { DaysAgo = 49, Source = ServiceRequestSources.Portal, Priority = ServiceRequestPriorities.Urgent, Category = "Electrical", Subject = "Power tripping in the whole flat",
                    Description = "The main breaker trips every few minutes. We have no power.",
                    ResponseHours = 1.5, DoneHours = 30, Rating = 6, Feedback = "Came quickly but power was only back the next day.",
                    Resolution = "Faulty RCD in the distribution board. Replaced and load tested.",
                    Materials = new[] { new Material("RCD 40A", 1, 95) } },

            new() { DaysAgo = 41, Source = ServiceRequestSources.Phone, Category = "Plumbing", Subject = "Bathroom floor drain blocked",
                    Description = "Water stands on the bathroom floor after a shower.",
                    ResponseHours = 6, DoneHours = 26, Rating = 10, Feedback = "Excellent service.",
                    Resolution = "Cleared the trap and the line with the machine." },

            new() { DaysAgo = 33, Source = ServiceRequestSources.WalkIn, Priority = ServiceRequestPriorities.High, Category = "Air Conditioning", Subject = "AC leaking water indoors",
                    Description = "Water drips from the indoor unit onto the sofa.",
                    ResponseHours = 4, DoneHours = 40, Rating = 9, Feedback = "Polite technician, cleaned up afterwards.",
                    Resolution = "Blocked drain line. Flushed and re-sloped the pipe.",
                    Materials = new[] { new Material("PVC drain pipe and clips", 1, 40) } },

            new() { DaysAgo = 26, Source = ServiceRequestSources.Portal, Category = "Appliances", Subject = "Oven not igniting",
                    Description = "The gas oven clicks but does not light. The hob works.", Status = ServiceRequestStatuses.Resolved,
                    ResponseHours = 10, DoneHours = 70, Rating = 3, Feedback = "The technician did not call before coming and there is still a smell of gas.",
                    Resolution = "Replaced the igniter. Tenant reports a smell: follow-up visit booked.",
                    Materials = new[] { new Material("Oven igniter", 1, 110) },
                    Comments = new[] { (false, "Low rating - call the tenant today and book the gas check.", true) } },

            new() { DaysAgo = 24, Source = ServiceRequestSources.Planned, Priority = ServiceRequestPriorities.High, Category = "Common Areas", Subject = "Six-monthly fire alarm and extinguisher check",
                    Description = "Test the panel, sounders and call points; check extinguisher pressure and tags.", Purpose = MaintenancePurposes.Preventive, Status = ServiceRequestStatuses.Resolved,
                    Plan = 1, DueInDays = 10, ResponseHours = 48, DoneHours = 336, Resolution = "Completed four days after the due date: the contractor rescheduled. Two extinguishers replaced.",
                    Materials = new[] { new Material("Fire extinguisher 6kg", 2, 120) } },

            new() { DaysAgo = 14, Source = ServiceRequestSources.WhatsApp, Category = "Carpentry", Subject = "Front door lock stiff",
                    Description = "The key is hard to turn and sometimes sticks.", Status = ServiceRequestStatuses.Resolved,
                    ResponseHours = 3, DoneHours = 28, Resolution = "Lubricated the cylinder and adjusted the strike plate. Waiting for the tenant's rating." },

            // ---- Open today: every stage, some already late -------------------------
            new() { DaysAgo = 9, Source = ServiceRequestSources.Portal, Priority = ServiceRequestPriorities.High, Category = "Plumbing", Subject = "Low water pressure in the shower",
                    Description = "The shower has had almost no pressure for a week. The basin is fine.", Status = ServiceRequestStatuses.InProgress,
                    ResponseHours = 5, Materials = new[] { new Material("Booster pump pressure switch", 1, 310) },
                    Comments = new[] { (false, "The pressure switch on the booster pump has failed. Part ordered, fitting on arrival.", false), (false, "Past target - chase the supplier.", true) } },

            new() { DaysAgo = 8, Source = ServiceRequestSources.Planned, Category = "Air Conditioning", Subject = "Quarterly AC service",
                    Description = "Clean filters and coils, check gas pressure and drains on every split unit.", Purpose = MaintenancePurposes.Preventive, Status = ServiceRequestStatuses.Submitted,
                    Plan = 0, DueInDays = 7 },

            new() { DaysAgo = 5, Source = ServiceRequestSources.Planned, Category = "Cleaning", Subject = "Yearly water tank cleaning",
                    Description = "Drain, clean and disinfect the roof and ground tanks; keep the municipality certificate.", Purpose = MaintenancePurposes.Preventive, Status = ServiceRequestStatuses.Acknowledged,
                    Plan = 2, DueInDays = 14, ResponseHours = 20,
                    Comments = new[] { (false, "Contractor booked for Saturday 6 am. Tenants to be told the water will be off until noon.", true) } },

            new() { DaysAgo = 4, Source = ServiceRequestSources.Phone, Category = "Air Conditioning", Subject = "Thermostat display blank",
                    Description = "The wall thermostat in the hall shows nothing and the AC will not start.", Status = ServiceRequestStatuses.InProgress,
                    ResponseHours = 20, Comments = new[] { (false, "Batteries replaced, still blank. New thermostat to be fitted tomorrow.", false) } },

            new() { DaysAgo = 3, Source = ServiceRequestSources.Management, Category = "Common Areas", Subject = "Repaint the staircase handrails",
                    Description = "Handrails on all floors are chipped. Rub down and repaint in the existing colour.", Purpose = MaintenancePurposes.Improvement, CommonArea = true,
                    Status = ServiceRequestStatuses.Acknowledged, ResponseHours = 26 },

            new() { DaysAgo = 2, Source = ServiceRequestSources.Portal, Priority = ServiceRequestPriorities.Urgent, Category = "Electrical", Subject = "Burning smell from the distribution board",
                    Description = "There is a burning plastic smell near the electrical board in the corridor.", Status = ServiceRequestStatuses.Acknowledged,
                    ResponseHours = 1, Comments = new[] { (false, "Board isolated and made safe. A breaker has overheated; replacement needs a power shutdown agreed with the tenant.", false), (true, "Tomorrow after 10 am is fine.", false) } },

            new() { DaysAgo = 0.9, Source = ServiceRequestSources.WhatsApp, Category = "Pest Control", Subject = "Ants on the balcony",
                    Description = "A line of ants is coming in from the balcony door.", Status = ServiceRequestStatuses.Submitted },

            new() { DaysAgo = 0.2, Source = ServiceRequestSources.Portal, Priority = ServiceRequestPriorities.High, Category = "Plumbing", Subject = "Toilet flush not working",
                    Description = "The flush button in the guest bathroom does nothing.", Status = ServiceRequestStatuses.Submitted, Unassigned = true },

            new() { DaysAgo = 0.08, Source = ServiceRequestSources.Email, Priority = ServiceRequestPriorities.Low, Category = "Other", Subject = "Parking bay number faded",
                    Description = "The number painted on our parking bay can no longer be read.", Status = ServiceRequestStatuses.Submitted, Unassigned = true }
        };
    }
}
