namespace MedicalApp.Features.ProgressForms.CreateProgressForm;

public record CreateProgressFormRequest(string Title, string? Description, IReadOnlyList<QuestionDto>? Questions);
