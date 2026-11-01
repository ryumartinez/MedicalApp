using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;

namespace MedicalApp.Common.Domain.ProgressForms;

[ValueObject<Guid>]
public readonly partial struct ProgressFormVersionId;

/// <summary>
/// An immutable published definition. Plans and answers refer to it, so its content never changes.
/// </summary>
public class ProgressFormVersion : Entity<ProgressFormVersionId>
{
    public ProgressFormId FormId { get; private set; }
    public DoctorId DoctorId { get; private set; }
    public int VersionNumber { get; private set; }
    public DateTimeOffset PublishedAt { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public IReadOnlyList<QuestionDefinition> Questions { get; private set; } = [];

    private ProgressFormVersion() { }

    internal static ProgressFormVersion Create(ProgressForm form, int versionNumber, DateTimeOffset publishedAt,
        IReadOnlyList<QuestionDefinition> questions) => new()
    {
        Id = ProgressFormVersionId.From(Guid.CreateVersion7()),
        FormId = form.Id,
        DoctorId = form.DoctorId,
        VersionNumber = versionNumber,
        PublishedAt = publishedAt,
        Title = form.Title,
        Description = form.Description,
        Questions = questions
    };

    public QuestionDefinition? FindQuestion(ProgressQuestionId id) => Questions.FirstOrDefault(question => question.Id == id);
}
