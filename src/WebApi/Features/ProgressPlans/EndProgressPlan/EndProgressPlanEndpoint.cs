using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressPlans.EndProgressPlan;

public class EndProgressPlanEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    ProgressAuthorizationService authorization,
    TimeProvider timeProvider) : Endpoint<EndProgressPlanRequest, PlanDto>
{
    public override void Configure()
    {
        Post("/{id}/end");
        Group<ProgressPlansGroup>();
        Policies(ProgressPolicies.Doctor);
        Description(x => x.WithName("EndProgressPlan").ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(EndProgressPlanRequest req, CancellationToken ct)
    {
        var result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => EndAsync(req, ct));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.OkAsync(PlanDto.From(result.Value), ct);
    }

    private async Task<ErrorOr<ProgressPlan>> EndAsync(EndProgressPlanRequest req, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        var doctorId = await identities.GetDoctorIdAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // The plan lock orders closure against draft saves and submissions.
        var plan = await dbContext.LockPlanAsync(ProgressPlanId.From(req.Id), ct);
        if (plan is null || !await authorization.CanManagePlanAsync(doctorId, plan, ct))
            return ProgressPlanErrors.NotFound;
        if (plan.Revision != req.Revision)
            return ProgressPlanErrors.Conflict;

        var ended = plan.End(timeProvider.GetUtcNow(), PlanEndReason.EndedByDoctor);
        if (ended.IsError)
            return ended.Errors;

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return plan;
    }
}
