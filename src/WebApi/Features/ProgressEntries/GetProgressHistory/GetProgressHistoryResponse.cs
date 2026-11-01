using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Features.ProgressEntries.GetProgressHistory;

public record GetProgressHistoryResponse(IReadOnlyList<GetProgressHistoryResponse.HistoryEntryDto> Items, int Total, int Page, int PageSize)
{
    public record HistoryEntryDto(
        Guid Id,
        Guid PlanId,
        Guid PatientId,
        DateOnly PeriodStartDate,
        DateOnly PeriodEndDate,
        DateTimeOffset PeriodStartUtc,
        DateTimeOffset PeriodEndUtc,
        EntryStatus Status,
        DateTimeOffset? SubmittedAt,
        bool IsLate,
        FormVersionRefDto FormVersion,
        IReadOnlyList<HistoryAnswerDto> Answers);

    public record FormVersionRefDto(Guid Id, Guid FormId, int VersionNumber, string Title);

    /// <summary>
    /// An answer interpreted through the entry's own form version.
    /// </summary>
    public record HistoryAnswerDto(
        Guid QuestionId,
        string QuestionKey,
        string QuestionLabel,
        QuestionType Type,
        string? Text,
        decimal? Number,
        Guid? OptionId,
        string? OptionLabel,
        AttachmentDto? Attachment);

    /// <summary>
    /// Metadata only. Request the content through GetMediaDownloadUrl.
    /// </summary>
    public record AttachmentDto(Guid Id, MediaKind Kind, string ContentType, long? Size, MediaAssetState State);
}
