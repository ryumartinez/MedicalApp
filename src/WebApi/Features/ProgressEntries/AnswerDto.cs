using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Features.ProgressEntries;

/// <summary>
/// One answer. Set exactly one value: Text, Number, OptionId, or MediaAssetId. Omit the answer to leave a question unanswered.
/// </summary>
public record AnswerDto(Guid QuestionId, string? Text = null, decimal? Number = null, Guid? OptionId = null, Guid? MediaAssetId = null)
{
    public ProgressAnswer ToAnswer() => new()
    {
        QuestionId = ProgressQuestionId.From(QuestionId),
        Text = Text,
        Number = Number,
        OptionId = OptionId is { } option ? ProgressQuestionOptionId.From(option) : null,
        MediaAssetId = MediaAssetId is { } asset ? Common.Domain.MediaAssets.MediaAssetId.From(asset) : null
    };

    public static AnswerDto From(ProgressAnswer answer) =>
        new(answer.QuestionId.Value, answer.Text, answer.Number, answer.OptionId?.Value, answer.MediaAssetId?.Value);
}

public record EntryDto(
    Guid Id,
    Guid PlanId,
    Guid PatientId,
    Guid FormVersionId,
    DateOnly PeriodStartDate,
    DateOnly PeriodEndDate,
    DateTimeOffset PeriodStartUtc,
    DateTimeOffset PeriodEndUtc,
    EntryStatus Status,
    DateTimeOffset? SubmittedAt,
    bool IsLate,
    uint Revision,
    IReadOnlyList<AnswerDto> Answers)
{
    public static EntryDto From(ProgressEntry entry) => new(
        entry.Id.Value, entry.PlanId.Value, entry.PatientId.Value, entry.FormVersionId.Value, entry.PeriodStartDate, entry.PeriodEndDate,
        entry.PeriodStartUtc, entry.PeriodEndUtc, entry.Status, entry.SubmittedAt, entry.IsLate, entry.Revision,
        entry.Answers.Select(AnswerDto.From).ToList());
}
