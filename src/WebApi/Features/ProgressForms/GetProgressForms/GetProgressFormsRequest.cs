namespace MedicalApp.Features.ProgressForms.GetProgressForms;

public record GetProgressFormsRequest(string? Search, int Page = 1, int PageSize = 20);
