namespace MedicalApp.Features.ProgressForms.DuplicateProgressForm;

public record DuplicateProgressFormResponse(Guid Id, Guid SourceFormId, uint Revision);
