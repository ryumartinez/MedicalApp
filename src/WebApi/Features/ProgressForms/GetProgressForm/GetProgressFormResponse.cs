namespace MedicalApp.Features.ProgressForms.GetProgressForm;

public record GetProgressFormResponse(
    Guid Id,
    string Title,
    string? Description,
    uint Revision,
    int LatestVersionNumber,
    Guid? SourceFormId,
    IReadOnlyList<QuestionDto> DraftQuestions,
    IReadOnlyList<GetProgressFormResponse.VersionDto> Versions)
{
    public record VersionDto(Guid Id, int VersionNumber, DateTimeOffset PublishedAt, string Title, string? Description,
        IReadOnlyList<QuestionDto> Questions);
}
