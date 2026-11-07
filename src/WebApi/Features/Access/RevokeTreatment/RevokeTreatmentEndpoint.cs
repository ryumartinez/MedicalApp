using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;

namespace MedicalApp.Features.Access.RevokeTreatment;

public class RevokeTreatmentEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : EndpointWithoutRequest<RevokeTreatmentResponse>
{
    public override void Configure()
    {
        Post("/treatments/{id:guid}/revoke");
        Group<AccessGroup>();
        Description(x => x.WithName("RevokeTreatment").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = DoctorPatientRelationshipId.From(Route<Guid>("id"));
        var result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => RevokeAsync(id, ct));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.OkAsync(result.Value, ct);
    }

    private async Task<ErrorOr<RevokeTreatmentResponse>> RevokeAsync(DoctorPatientRelationshipId id, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // Lock order: treatment, then plans. Submissions on these plans wait for, or finish before, this closure.
        var relationship = await dbContext.LockTreatmentAsync(id, ct);
        if (relationship is null)
            return AccessErrors.TreatmentNotFound;

        relationship.Revoke();
        // Only this doctor's plans for this patient close. Plans of other doctors stay open.
        var plans = await dbContext.LockOpenPlansAsync(relationship.DoctorId, relationship.PatientId, ct);
        var now = timeProvider.GetUtcNow();
        foreach (var plan in plans)
            plan.End(now, PlanEndReason.TreatmentRevoked);

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return new RevokeTreatmentResponse(relationship.Id.Value, relationship.IsActive, plans.Select(plan => plan.Id.Value).ToList());
    }
}
