using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.GetDueProgressEntries;

public class GetDueProgressEntriesEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, TimeProvider timeProvider)
    : Endpoint<GetDueProgressEntriesRequest, GetDueProgressEntriesResponse>
{
    public override void Configure()
    {
        Get("/due");
        Group<ProgressEntriesGroup>();
        Policies(ProgressPolicies.Patient);
        Description(x => x.WithName("GetDueProgressEntries").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetDueProgressEntriesRequest req, CancellationToken ct)
    {
        var patientId = await identities.GetPatientIdAsync(ct);
        var plans = dbContext.ProgressPlans.AsNoTracking().Where(plan => plan.PatientId == patientId && plan.EndedAt == null);
        if (req.PlanId is { } requested)
        {
            var planId = ProgressPlanId.From(requested);
            plans = plans.Where(plan => plan.Id == planId);
        }

        var planList = await plans.OrderBy(plan => plan.Id).ToListAsync(ct);
        var planIds = planList.Select(plan => plan.Id).ToList();
        var entries = (await dbContext.ProgressEntries.AsNoTracking().Where(entry => planIds.Contains(entry.PlanId)).ToListAsync(ct))
            .ToDictionary(entry => (entry.PlanId, entry.PeriodStartDate));

        var now = timeProvider.GetUtcNow();
        var items = new List<GetDueProgressEntriesResponse.DuePeriodDto>();
        foreach (var plan in planList)
        {
            foreach (var period in plan.Schedule.PeriodsStartedBefore(now))
            {
                entries.TryGetValue((plan.Id, period.StartDate), out var entry);
                var state = plan.Classify(period, entry?.SubmittedAt, now);
                if (state is not (PeriodState.Due or PeriodState.Missed))
                    continue;
                items.Add(new GetDueProgressEntriesResponse.DuePeriodDto(plan.Id.Value, period.StartDate, period.EndDate,
                    period.StartUtc, period.EndUtc, state, entry?.Id.Value, entry?.Status, entry?.Revision));
            }
        }

        await Send.OkAsync(new GetDueProgressEntriesResponse(items), ct);
    }
}
