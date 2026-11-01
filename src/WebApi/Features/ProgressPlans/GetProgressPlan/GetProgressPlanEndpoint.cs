using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Interfaces;
using MedicalApp.Features.ProgressForms;

namespace MedicalApp.Features.ProgressPlans.GetProgressPlan;

public class GetProgressPlanEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    ProgressAuthorizationService authorization) : Endpoint<GetProgressPlanRequest, GetProgressPlanResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<ProgressPlansGroup>();
        Description(x => x.WithName("GetProgressPlan").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(GetProgressPlanRequest req, CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var planId = ProgressPlanId.From(req.Id);
        var plan = await authorization.PlansVisibleTo(identity).AsNoTracking().SingleOrDefaultAsync(p => p.Id == planId, ct);
        if (plan is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var version = await dbContext.ProgressFormVersions.AsNoTracking().SingleAsync(v => v.Id == plan.FormVersionId, ct);
        await Send.OkAsync(new GetProgressPlanResponse(PlanDto.From(plan), new GetProgressPlanResponse.FormVersionDto(
            version.Id.Value, version.FormId.Value, version.VersionNumber, version.PublishedAt, version.Title, version.Description,
            version.Questions.Select(QuestionDto.From).ToList())), ct);
    }
}
