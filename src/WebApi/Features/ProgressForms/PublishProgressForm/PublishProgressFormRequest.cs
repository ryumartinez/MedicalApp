namespace MedicalApp.Features.ProgressForms.PublishProgressForm;

/// <param name="Revision">The draft revision the caller reviewed. A stale revision returns 409.</param>
public record PublishProgressFormRequest(Guid Id, uint Revision);
