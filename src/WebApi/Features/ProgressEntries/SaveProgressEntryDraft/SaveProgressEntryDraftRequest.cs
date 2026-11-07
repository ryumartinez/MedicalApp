namespace MedicalApp.Features.ProgressEntries.SaveProgressEntryDraft;

/// <param name="PeriodStartDate">The local start date of a scheduled period of the plan.</param>
/// <param name="Revision">Omit to create the period's entry. Send the revision you read to update it.</param>
public record SaveProgressEntryDraftRequest(Guid PlanId, DateOnly PeriodStartDate, uint? Revision, IReadOnlyList<AnswerDto>? Answers);
