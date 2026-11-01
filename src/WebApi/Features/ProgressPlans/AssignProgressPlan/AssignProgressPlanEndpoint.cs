using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressPlans.AssignProgressPlan;

public class AssignProgressPlanEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    ProgressAuthorizationService authorization,
    TimeProvider timeProvider) : Endpoint<AssignProgressPlanRequest, PlanDto>
{
    public override void Configure()
    {
        Post("/");
        Group<ProgressPlansGroup>();
        Policies(ProgressPolicies.Doctor);
        Description(x => x.WithName("AssignProgressPlan")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(AssignProgressPlanRequest req, CancellationToken ct)
    {
        var result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => AssignAsync(req, ct));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.CreatedAtAsync("GetProgressPlan", new { id = result.Value.Id.Value }, PlanDto.From(result.Value), cancellation: ct);
    }

    private async Task<ErrorOr<ProgressPlan>> AssignAsync(AssignProgressPlanRequest req, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var patientId = PatientId.From(req.PatientId);

        // Only your own published versions. Drafts are not versions, so they cannot be assigned.
        var version = await dbContext.ProgressFormVersions.AsNoTracking()
            .WithSpecification(ProgressFormVersionSpec.OwnedById(ProgressFormVersionId.From(req.FormVersionId), doctorId))
            .SingleOrDefaultAsync(ct);
        if (version is null)
            return ProgressFormErrors.VersionNotFound;

        // Treatment, not appointments, establishes access. An untreated patient is indistinguishable from a missing one.
        if (!await authorization.HasActiveTreatmentAsync(doctorId, patientId, ct))
            return ProgressPlanErrors.NoTreatment;

        var schedule = ProgressSchedule.Create(req.Frequency, req.TimeZone, req.StartDate, req.EndDate);
        if (schedule.IsError)
            return schedule.Errors;

        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var plan = ProgressPlan.Create(doctorId, patientId, version, schedule.Value);

        if (req.ReplacesPlanId is { } replacedId)
        {
            // Same lock as closure and submission, so no entry change commits on the old plan after it ends.
            var replaced = await dbContext.LockPlanAsync(ProgressPlanId.From(replacedId), ct);
            if (replaced is null || replaced.DoctorId != doctorId || replaced.PatientId != patientId || !replaced.IsOpen)
                return ProgressPlanErrors.ReplacedPlanMismatch;
            dbContext.ProgressPlans.Add(plan);
            await dbContext.SaveChangesAsync(ct);
            replaced.End(timeProvider.GetUtcNow(), PlanEndReason.Replaced, plan.Id);
        }
        else
        {
            dbContext.ProgressPlans.Add(plan);
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return plan;
    }
}
