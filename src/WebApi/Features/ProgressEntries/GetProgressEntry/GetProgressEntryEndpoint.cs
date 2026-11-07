using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.GetProgressEntry;

public class GetProgressEntryEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, ProgressAuthorizationService authorization)
    : Endpoint<GetProgressEntryRequest, EntryDto>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<ProgressEntriesGroup>();
        Description(x => x.WithName("GetProgressEntry").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(GetProgressEntryRequest req, CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var entryId = ProgressEntryId.From(req.Id);
        var visiblePlans = authorization.PlansVisibleTo(identity).Select(plan => plan.Id);
        var entry = await dbContext.ProgressEntries.AsNoTracking()
            .Where(e => e.Id == entryId && visiblePlans.Contains(e.PlanId))
            .SingleOrDefaultAsync(ct);
        if (entry is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(EntryDto.From(entry), ct);
    }
}
