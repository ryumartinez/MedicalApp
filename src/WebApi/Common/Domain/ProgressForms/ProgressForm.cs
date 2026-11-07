using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.MediaAssets;

namespace MedicalApp.Common.Domain.ProgressForms;

[ValueObject<Guid>]
public readonly partial struct ProgressFormId;

/// <summary>
/// A doctor's reusable form. Owns an editable draft. Publication copies the draft into an immutable <see cref="ProgressFormVersion"/>.
/// </summary>
public class ProgressForm : Entity<ProgressFormId>
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int MaxQuestions = 50;

    public DoctorId DoctorId { get; private set; }
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public IReadOnlyList<QuestionDefinition> DraftQuestions { get; private set; } = [];

    /// <summary>
    /// Number of the latest published version, or zero.
    /// </summary>
    public int LatestVersionNumber { get; private set; }

    public ProgressFormId? SourceFormId { get; private set; }

    /// <summary>
    /// Concurrency token. Changes with every committed update.
    /// </summary>
    public uint Revision { get; private set; }

    private ProgressForm() { }

    public static ErrorOr<ProgressForm> Create(DoctorId doctorId, string title, string? description, IReadOnlyList<QuestionDefinition> questions)
    {
        var errors = ValidateDraft(title, description, questions);
        if (errors.Count != 0)
            return errors;

        return new ProgressForm
        {
            Id = ProgressFormId.From(Guid.CreateVersion7()),
            DoctorId = doctorId,
            Title = title.Trim(),
            Description = Normalize(description),
            DraftQuestions = questions
        };
    }

    public ErrorOr<Success> UpdateDraft(string title, string? description, IReadOnlyList<QuestionDefinition> questions)
    {
        var errors = ValidateDraft(title, description, questions);
        if (errors.Count != 0)
            return errors;

        Title = title.Trim();
        Description = Normalize(description);
        DraftQuestions = questions;
        return Result.Success;
    }

    /// <summary>
    /// Copies the current draft into a new, independent form owned by the same doctor.
    /// </summary>
    public ProgressForm Duplicate() => new()
    {
        Id = ProgressFormId.From(Guid.CreateVersion7()),
        DoctorId = DoctorId,
        Title = Title,
        Description = Description,
        DraftQuestions = DraftQuestions,
        SourceFormId = Id
    };

    /// <summary>
    /// Validates the whole draft and creates the next immutable version.
    /// Questions keep their keys and option IDs and receive new version-specific IDs.
    /// </summary>
    public ErrorOr<ProgressFormVersion> Publish(DateTimeOffset publishedAt)
    {
        var errors = ValidateForPublication(Title, Description, DraftQuestions);
        if (errors.Count != 0)
            return errors;

        var questions = DraftQuestions
            .Select(question => question with { Id = ProgressQuestionId.From(Guid.CreateVersion7()) })
            .ToList();
        LatestVersionNumber++;
        return ProgressFormVersion.Create(this, LatestVersionNumber, publishedAt, questions);
    }

    /// <summary>
    /// Limits that every saved draft must respect. Drafts can be incomplete.
    /// </summary>
    public static List<Error> ValidateDraft(string? title, string? description, IReadOnlyList<QuestionDefinition>? questions)
    {
        var errors = new List<Error>();
        if (string.IsNullOrWhiteSpace(title))
            errors.Add(ProgressFormErrors.TitleRequired);
        else if (title.Trim().Length > TitleMaxLength)
            errors.Add(ProgressFormErrors.TooLong("Title", TitleMaxLength));
        if (description is { Length: > DescriptionMaxLength })
            errors.Add(ProgressFormErrors.TooLong("Description", DescriptionMaxLength));

        questions ??= [];
        if (questions.Count > MaxQuestions)
            errors.Add(ProgressFormErrors.TooManyQuestions);

        foreach (var (question, index) in questions.Select((q, i) => (q, i)))
        {
            var field = $"Questions[{index}]";
            if (string.IsNullOrWhiteSpace(question.Key) || question.Key.Length > QuestionDefinition.KeyMaxLength
                || !System.Text.RegularExpressions.Regex.IsMatch(question.Key, QuestionDefinition.KeyPattern))
                errors.Add(ProgressFormErrors.InvalidKey(field));
            if (!Enum.IsDefined(question.Type))
                errors.Add(ProgressFormErrors.InvalidType(field));
            if (question.Label is { Length: > QuestionDefinition.LabelMaxLength })
                errors.Add(ProgressFormErrors.TooLong($"{field}.Label", QuestionDefinition.LabelMaxLength));
            if (question.Instructions is { Length: > QuestionDefinition.InstructionsMaxLength })
                errors.Add(ProgressFormErrors.TooLong($"{field}.Instructions", QuestionDefinition.InstructionsMaxLength));
            if (question.MinLabel is { Length: > QuestionDefinition.EndpointLabelMaxLength }
                || question.MaxLabel is { Length: > QuestionDefinition.EndpointLabelMaxLength })
                errors.Add(ProgressFormErrors.TooLong($"{field}.EndpointLabels", QuestionDefinition.EndpointLabelMaxLength));
            if (question.Options.Count > QuestionDefinition.MaxOptions)
                errors.Add(ProgressFormErrors.TooManyOptions(field));
            if (question.Options.Any(option => option.Label is null || option.Label.Length > ChoiceOption.LabelMaxLength))
                errors.Add(ProgressFormErrors.TooLong($"{field}.Options", ChoiceOption.LabelMaxLength));
            if (question.Options.Select(option => option.Id).Distinct().Count() != question.Options.Count)
                errors.Add(ProgressFormErrors.DuplicateOption(field));
            if (question.AllowedContentTypes.Any(type => !MediaAsset.IsSupportedContentType(type)))
                errors.Add(ProgressFormErrors.UnsupportedContentType(field));
            if (!HasOnlyTypeMembers(question))
                errors.Add(ProgressFormErrors.MembersDoNotMatchType(field));
        }

        if (questions.Select(question => question.Key).Distinct(StringComparer.Ordinal).Count() != questions.Count)
            errors.Add(ProgressFormErrors.DuplicateKey);
        return errors;
    }

    /// <summary>
    /// Whole-form rules for publication, including every type-specific rule.
    /// </summary>
    public static List<Error> ValidateForPublication(string? title, string? description, IReadOnlyList<QuestionDefinition> questions)
    {
        var errors = ValidateDraft(title, description, questions);
        if (questions.Count == 0)
            errors.Add(ProgressFormErrors.NoQuestions);

        foreach (var (question, index) in questions.Select((q, i) => (q, i)))
        {
            var field = $"Questions[{index}]";
            if (string.IsNullOrWhiteSpace(question.Label))
                errors.Add(ProgressFormErrors.LabelRequired(field));

            switch (question.Type)
            {
                case QuestionType.NumericScale:
                    if (question.Min is not { } min || question.Max is not { } max || min >= max)
                        errors.Add(ProgressFormErrors.InvalidScale(field));
                    else if (question.Step is { } step && (step <= 0 || (max - min) % step != 0))
                        errors.Add(ProgressFormErrors.InvalidStep(field));
                    break;
                case QuestionType.SingleChoice:
                    if (question.Options.Count < QuestionDefinition.MinOptions)
                        errors.Add(ProgressFormErrors.TooFewOptions(field));
                    if (question.Options.Any(option => string.IsNullOrWhiteSpace(option.Label)))
                        errors.Add(ProgressFormErrors.LabelRequired($"{field}.Options"));
                    break;
                case QuestionType.Image:
                case QuestionType.Video:
                    var kind = question.Type == QuestionType.Image ? MediaKind.Image : MediaKind.Video;
                    if (question.AllowedContentTypes.Count == 0
                        || question.AllowedContentTypes.Any(type => MediaAsset.KindOf(type) != kind))
                        errors.Add(ProgressFormErrors.UnsupportedContentType(field));
                    break;
            }
        }

        return errors;
    }

    private static bool HasOnlyTypeMembers(QuestionDefinition question)
    {
        var numeric = question.Min is not null || question.Max is not null || question.Step is not null
                      || question.MinLabel is not null || question.MaxLabel is not null;
        var choice = question.Options.Count != 0;
        var attachment = question.AllowedContentTypes.Count != 0;
        return question.Type switch
        {
            QuestionType.Text => !numeric && !choice && !attachment,
            QuestionType.NumericScale => !choice && !attachment,
            QuestionType.SingleChoice => !numeric && !attachment,
            QuestionType.Image or QuestionType.Video => !numeric && !choice,
            _ => false
        };
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
