namespace MedicalApp.Features.ProgressForms.GetProgressForms;

public record GetProgressFormsResponse(IReadOnlyList<GetProgressFormsResponse.FormDto> Items, int Total, int Page, int PageSize)
{
    public record FormDto(Guid Id, string Title, int LatestVersionNumber, int QuestionCount, Guid? SourceFormId);
}
