namespace MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;

/// <param name="Revision">The revision the caller read. A stale revision returns 409.</param>
public record UpdateProgressFormDraftRequest(Guid Id, uint Revision, string Title, string? Description, IReadOnlyList<QuestionDto>? Questions);
