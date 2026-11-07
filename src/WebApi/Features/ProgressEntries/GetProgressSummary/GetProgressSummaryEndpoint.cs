using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.GetProgressSummary;

public class GetProgressSummaryEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    ProgressAuthorizationService authorization,
    TimeProvider timeProvider) : Endpoint<GetProgressSummaryRequest, GetProgressSummaryResponse>
{
    public override void Configure()
    {
        Get("/summary");
        Group<ProgressEntriesGroup>();
        Description(x => x.WithName("GetProgressSummary").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetProgressSummaryRequest req, CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var scope = ProgressScope.Plans(authorization, identity, req.PlanId, req.PatientId);
        if (scope.IsError)
        {
            await HttpContext.SendErrorsAsync(scope.Errors, ct);
            return;
        }

        var plans = await scope.Value.AsNoTracking().OrderBy(plan => plan.StartDate).ThenBy(plan => plan.Id).ToListAsync(ct);
        var planIds = plans.Select(plan => plan.Id).ToList();
        var entries = await dbContext.ProgressEntries.AsNoTracking().Where(entry => planIds.Contains(entry.PlanId)).ToListAsync(ct);
        var versionIds = plans.Select(plan => plan.FormVersionId).Distinct().ToList();
        var versions = await dbContext.ProgressFormVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);

        var now = timeProvider.GetUtcNow();
        var summaries = plans.Select(plan => Summarize(plan, entries.Where(e => e.PlanId == plan.Id).ToList(), now)).ToList();
        var trends = versions.Values
            .OrderBy(version => version.FormId).ThenBy(version => version.VersionNumber)
            .SelectMany(version => version.Questions
                .Where(question => question.Type == QuestionType.NumericScale)
                .Select(question => Trend(version, question, entries)))
            .ToList();

        await Send.OkAsync(new GetProgressSummaryResponse(summaries, trends), ct);
    }

    private static GetProgressSummaryResponse.PlanSummaryDto Summarize(ProgressPlan plan, List<Common.Domain.ProgressEntries.ProgressEntry> entries, DateTimeOffset now)
    {
        var byPeriod = entries.ToDictionary(entry => entry.PeriodStartDate);
        var states = new List<PeriodState>();
        var drafts = 0;
        foreach (var period in plan.Schedule.PeriodsStartedBefore(now))
        {
            byPeriod.TryGetValue(period.StartDate, out var entry);
            var state = plan.Classify(period, entry?.SubmittedAt, now);
            states.Add(state);
            if (entry is { IsSubmitted: false })
                drafts++;
        }

        int? future = null;
        if (plan.EndDate is not null && plan.IsOpen)
            future = plan.Schedule.PeriodsStartedBefore(DateTimeOffset.MaxValue).Count() - states.Count;
        else if (!plan.IsOpen)
            future = 0;

        return new GetProgressSummaryResponse.PlanSummaryDto(
            plan.Id.Value, plan.FormVersionId.Value,
            Completed: states.Count(s => s is PeriodState.Completed or PeriodState.CompletedLate),
            CompletedLate: states.Count(s => s == PeriodState.CompletedLate),
            Missed: states.Count(s => s == PeriodState.Missed),
            Due: states.Count(s => s == PeriodState.Due),
            Closed: states.Count(s => s == PeriodState.Closed),
            Future: future,
            IncompleteDrafts: drafts);
    }

    private static GetProgressSummaryResponse.NumericTrendDto Trend(ProgressFormVersion version, QuestionDefinition question,
        List<Common.Domain.ProgressEntries.ProgressEntry> entries)
    {
        var points = entries
            .Where(entry => entry.IsSubmitted && entry.FormVersionId == version.Id)
            .SelectMany(entry => entry.Answers
                .Where(answer => answer.QuestionId == question.Id && answer.Number is not null)
                .Select(answer => new GetProgressSummaryResponse.TrendPointDto(entry.PlanId.Value, entry.PeriodStartDate, answer.Number!.Value,
                    entry.SubmittedAt!.Value, entry.IsLate)))
            .OrderBy(point => point.PeriodStartDate).ThenBy(point => point.PlanId)
            .ToList();

        return new GetProgressSummaryResponse.NumericTrendDto(question.Key, question.Id.Value, question.Label, version.Id.Value,
            version.VersionNumber, question.Min!.Value, question.Max!.Value, question.Step, question.MinLabel, question.MaxLabel, points);
    }
}
