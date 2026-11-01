using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.SubmitProgressEntry;

public class SubmitProgressEntryEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, TimeProvider timeProvider)
    : EndpointWithoutRequest<EntryDto>
{
    public override void Configure()
    {
        Post("/{id:guid}/submit");
        Group<ProgressEntriesGroup>();
        Policies(ProgressPolicies.Patient);
        Description(x => x.WithName("SubmitProgressEntry")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var patientId = await identities.GetPatientIdAsync(ct);
        var entryId = ProgressEntryId.From(Route<Guid>("id"));
        // Optional: when set, submission requires that the draft still has this revision.
        var revision = Query<uint?>("revision", isRequired: false);

        // A retry after a committed submission returns the stored result without another write.
        var existing = await dbContext.ProgressEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == entryId, ct);
        if (existing is null || existing.PatientId != patientId)
        {
            await Send.NotFoundAsync(ct);
            return;
        }
        if (existing.IsSubmitted)
        {
            await Send.OkAsync(EntryDto.From(existing), ct);
            return;
        }

        var result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => SubmitAsync(existing.PlanId, entryId, revision, ct));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.OkAsync(EntryDto.From(result.Value), ct);
    }

    /// <summary>
    /// The shared submission transaction. Locks the plan, the entry, and then the referenced assets in ID order,
    /// validates everything against committed state, and commits the submitted entry and attached assets together.
    /// </summary>
    private async Task<ErrorOr<ProgressEntry>> SubmitAsync(ProgressPlanId planId, ProgressEntryId entryId, uint? revision, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        var plan = await dbContext.LockPlanAsync(planId, ct);
        var entry = await dbContext.LockEntryAsync(entryId, ct);
        if (plan is null || entry is null)
            return ProgressEntryErrors.NotFound;

        // A concurrent submission committed while this one waited for the lock.
        if (entry.IsSubmitted)
            return entry;
        if (revision is { } expected && expected != entry.Revision)
            return ProgressEntryErrors.Conflict;

        var now = timeProvider.GetUtcNow();
        if (!plan.IsOpen)
            return ProgressEntryErrors.PlanClosed;
        if (!plan.AcceptsEntriesFor(entry.Period, now))
            return ProgressEntryErrors.PeriodNotStarted;

        var assets = await dbContext.LockAssetsAsync(AttachmentEligibility.AssetIds(entry.Answers).ToList(), ct);
        var unavailable = AttachmentEligibility.Check(entry, entry.Answers, assets);
        if (unavailable.Count != 0)
            return unavailable;

        var version = await dbContext.ProgressFormVersions.AsNoTracking().SingleAsync(v => v.Id == entry.FormVersionId, ct);
        var submitted = entry.Submit(version, now);
        if (submitted.IsError)
            return submitted.Errors;

        // Only submission attaches assets, in the same transaction as the entry's submitted state.
        foreach (var asset in assets)
            asset.Attach(now);

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return entry;
    }
}
