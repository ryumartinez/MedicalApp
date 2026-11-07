using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Features.ProgressForms;

/// <summary>
/// A question in a draft or published version. Set only the members for the question's type.
/// Omit <see cref="Id"/> and option IDs for new items; keep option IDs to preserve their identity across versions.
/// </summary>
public record QuestionDto(
    Guid? Id,
    string Key,
    QuestionType Type,
    string Label,
    string? Instructions,
    bool Required,
    int? Min = null,
    int? Max = null,
    int? Step = null,
    string? MinLabel = null,
    string? MaxLabel = null,
    IReadOnlyList<QuestionDto.OptionDto>? Options = null,
    IReadOnlyList<string>? AllowedContentTypes = null)
{
    public record OptionDto(Guid? Id, string Label);

    public QuestionDefinition ToDefinition() => new()
    {
        Id = ProgressQuestionId.From(Id is { } id && id != Guid.Empty ? id : Guid.CreateVersion7()),
        Key = Key?.Trim() ?? string.Empty,
        Type = Type,
        Label = Label?.Trim() ?? string.Empty,
        Instructions = string.IsNullOrWhiteSpace(Instructions) ? null : Instructions.Trim(),
        Required = Required,
        Min = Min,
        Max = Max,
        Step = Step,
        MinLabel = string.IsNullOrWhiteSpace(MinLabel) ? null : MinLabel.Trim(),
        MaxLabel = string.IsNullOrWhiteSpace(MaxLabel) ? null : MaxLabel.Trim(),
        Options = (Options ?? []).Select(option => new ChoiceOption(
            ProgressQuestionOptionId.From(option.Id is { } optionId && optionId != Guid.Empty ? optionId : Guid.CreateVersion7()),
            option.Label?.Trim() ?? string.Empty)).ToList(),
        AllowedContentTypes = (AllowedContentTypes ?? []).Select(type => type.Trim().ToLowerInvariant()).Distinct().ToList()
    };

    public static QuestionDto From(QuestionDefinition question) => new(
        question.Id.Value, question.Key, question.Type, question.Label, question.Instructions, question.Required,
        question.Min, question.Max, question.Step, question.MinLabel, question.MaxLabel,
        question.Options.Select(option => new OptionDto(option.Id.Value, option.Label)).ToList(),
        question.AllowedContentTypes);

    public static IReadOnlyList<QuestionDefinition> ToDefinitions(IReadOnlyList<QuestionDto>? questions) =>
        (questions ?? []).Select(question => question.ToDefinition()).ToList();
}
