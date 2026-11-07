namespace MedicalApp.Features.ProgressEntries.GetProgressSummary;

public record GetProgressSummaryResponse(
    IReadOnlyList<GetProgressSummaryResponse.PlanSummaryDto> Plans,
    IReadOnlyList<GetProgressSummaryResponse.NumericTrendDto> NumericTrends)
{
    /// <param name="Completed">Submitted periods, including late ones.</param>
    /// <param name="CompletedLate">Submitted after the period ended. Included in Completed.</param>
    /// <param name="Missed">Ended without a submission while the plan was open. Drafts do not count as submitted.</param>
    /// <param name="Due">The current period, started and not submitted.</param>
    /// <param name="Closed">Not ended when the plan closed and not submitted. Never counted as missed.</param>
    /// <param name="Future">Scheduled periods that have not started. Null for plans without an end date.</param>
    /// <param name="IncompleteDrafts">Unsubmitted periods with a saved draft.</param>
    public record PlanSummaryDto(
        Guid PlanId,
        Guid FormVersionId,
        int Completed,
        int CompletedLate,
        int Missed,
        int Due,
        int Closed,
        int? Future,
        int IncompleteDrafts);

    /// <summary>
    /// One numeric question of one published version. Series from different versions stay separate even with the same key.
    /// </summary>
    public record NumericTrendDto(
        string QuestionKey,
        Guid QuestionId,
        string QuestionLabel,
        Guid FormVersionId,
        int VersionNumber,
        int Min,
        int Max,
        int? Step,
        string? MinLabel,
        string? MaxLabel,
        IReadOnlyList<TrendPointDto> Points);

    public record TrendPointDto(Guid PlanId, DateOnly PeriodStartDate, decimal Value, DateTimeOffset SubmittedAt, bool IsLate);
}
