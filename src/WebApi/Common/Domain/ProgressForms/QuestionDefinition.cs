using MedicalApp.Common.Domain.Base.Interfaces;

namespace MedicalApp.Common.Domain.ProgressForms;

// Version-specific question identifier. A question keeps its stable Key across versions.
[ValueObject<Guid>]
public readonly partial struct ProgressQuestionId;

[ValueObject<Guid>]
public readonly partial struct ProgressQuestionOptionId;

/// <summary>
/// One question in a draft or a published version. Published versions never change their questions.
/// Only the members for the question's type carry values.
/// </summary>
public sealed record QuestionDefinition : IValueObject
{
    public const int KeyMaxLength = 64;
    public const string KeyPattern = "^[a-z][a-z0-9_-]*$";
    public const int LabelMaxLength = 500;
    public const int InstructionsMaxLength = 2000;
    public const int EndpointLabelMaxLength = 100;
    public const int MinOptions = 2;
    public const int MaxOptions = 20;

    /// <summary>
    /// Version-specific ID. Drafts carry an ID too, but publication assigns a new one.
    /// </summary>
    public required ProgressQuestionId Id { get; init; }

    /// <summary>
    /// Stable key that identifies the same logical question across versions.
    /// </summary>
    public required string Key { get; init; }

    public required QuestionType Type { get; init; }
    public required string Label { get; init; }
    public string? Instructions { get; init; }
    public bool Required { get; init; }

    // Numeric scale
    public int? Min { get; init; }
    public int? Max { get; init; }
    public int? Step { get; init; }
    public string? MinLabel { get; init; }
    public string? MaxLabel { get; init; }

    // Single choice
    public IReadOnlyList<ChoiceOption> Options { get; init; } = [];

    // Image and video. One file per answer.
    public IReadOnlyList<string> AllowedContentTypes { get; init; } = [];

    public bool IsAttachment => Type is QuestionType.Image or QuestionType.Video;

    public bool AcceptsNumber(decimal value) =>
        Type == QuestionType.NumericScale
        && Min is { } min && Max is { } max
        && value == decimal.Truncate(value)
        && value >= min && value <= max
        && (Step is not { } step || (value - min) % step == 0);
}

public sealed record ChoiceOption(ProgressQuestionOptionId Id, string Label) : IValueObject
{
    public const int LabelMaxLength = 100;
}
