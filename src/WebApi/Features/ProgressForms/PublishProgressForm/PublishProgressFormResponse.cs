namespace MedicalApp.Features.ProgressForms.PublishProgressForm;

public record PublishProgressFormResponse(Guid FormId, Guid VersionId, int VersionNumber, DateTimeOffset PublishedAt, uint Revision);
