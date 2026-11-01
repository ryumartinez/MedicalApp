using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;

namespace MedicalApp.Features.Access.RevokePlanGrant;

public class RevokePlanGrantEndpoint(ApplicationDbContext dbContext) : EndpointWithoutRequest<RevokePlanGrantResponse>
{
    public override void Configure()
    {
        Post("/plan-grants/{id:guid}/revoke");
        Group<AccessGroup>();
        Description(x => x.WithName("RevokePlanGrant").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = ProgressPlanGrantId.From(Route<Guid>("id"));
        var grant = await dbContext.ProgressPlanGrants.SingleOrDefaultAsync(g => g.Id == id, ct);
        if (grant is null)
        {
            await HttpContext.SendErrorsAsync([AccessErrors.GrantNotFound], ct);
            return;
        }

        if (grant.IsActive)
        {
            grant.Revoke();
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.OkAsync(new RevokePlanGrantResponse(grant.Id.Value, grant.IsActive), ct);
    }
}
