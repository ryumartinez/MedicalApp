using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.SaveProgressEntryDraft;

public class SaveProgressEntryDraftEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, TimeProvider timeProvider)
    : Endpoint<SaveProgressEntryDraftRequest, EntryDto>
{
    public override void Configure()
    {
        Put("/draft");
        Group<ProgressEntriesGroup>();
        Policies(ProgressPolicies.Patient);
        Description(x => x.WithName("SaveProgressEntryDraft")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(SaveProgressEntryDraftRequest req, CancellationToken ct)
    {
        var result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => SaveAsync(req, ct));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.OkAsync(EntryDto.From(result.Value), ct);
    }

    private async Task<ErrorOr<ProgressEntry>> SaveAsync(SaveProgressEntryDraftRequest req, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        // The acting patient always comes from the caller's mapping, never from the request.
        var patientId = await identities.GetPatientIdAsync(ct);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // Lock order: plan, then entry. Closure and submission take the same plan lock.
        var plan = await dbContext.LockPlanAsync(ProgressPlanId.From(req.PlanId), ct);
        if (plan is null || plan.PatientId != patientId)
            return ProgressPlanErrors.NotFound;
        if (!plan.IsOpen)
            return ProgressEntryErrors.PlanClosed;

        var period = plan.Schedule.PeriodStartingOn(req.PeriodStartDate);
        if (period is null)
            return ProgressEntryErrors.PeriodNotScheduled;
        if (!plan.AcceptsEntriesFor(period, timeProvider.GetUtcNow()))
            return ProgressEntryErrors.PeriodNotStarted;

        var version = await dbContext.ProgressFormVersions.AsNoTracking().SingleAsync(v => v.Id == plan.FormVersionId, ct);
        var answers = (req.Answers ?? []).Select(answer => answer.ToAnswer()).ToList();

        var entry = await dbContext.LockEntryAsync(plan.Id, period.StartDate, ct);
        if (entry is null)
        {
            // The plan lock serializes creation, so the period gets one entry. The unique index backs this up.
            if (req.Revision is not null)
                return ProgressEntryErrors.Conflict;
            entry = ProgressEntry.Create(plan, period);
            dbContext.ProgressEntries.Add(entry);
        }
        else
        {
            if (entry.IsSubmitted)
                return ProgressEntryErrors.AlreadySubmitted;
            if (req.Revision != entry.Revision)
                return ProgressEntryErrors.Conflict;
        }

        var saved = entry.SaveDraft(version, answers);
        if (saved.IsError)
            return saved.Errors;

        var assetIds = AttachmentEligibility.AssetIds(answers);
        if (assetIds.Count != 0)
        {
            var assets = await dbContext.MediaAssets.AsNoTracking().Where(asset => assetIds.Contains(asset.Id)).ToListAsync(ct);
            var unavailable = AttachmentEligibility.Check(entry, answers, assets);
            if (unavailable.Count != 0)
                return unavailable;
        }

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return entry;
    }
}
