using MedicalApp.Common.Domain.Base.Interfaces;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Common.Domain.ProgressEntries;

/// <summary>
/// One answer. It holds exactly one value: text, a whole number, an option, or a media asset.
/// An omitted answer has no record; an answer record always carries a value.
/// </summary>
public sealed record ProgressAnswer : IValueObject
{
    public const int TextMaxLength = 4000;

    public ProgressQuestionId QuestionId { get; init; }
    public string? Text { get; init; }
    public decimal? Number { get; init; }
    public ProgressQuestionOptionId? OptionId { get; init; }
    public MediaAssetId? MediaAssetId { get; init; }

    public int ValueCount =>
        (Text is null ? 0 : 1) + (Number is null ? 0 : 1) + (OptionId is null ? 0 : 1) + (MediaAssetId is null ? 0 : 1);

    /// <summary>
    /// Checks the answer against its question in the assigned version.
    /// </summary>
    public Error? Validate(QuestionDefinition question, string field)
    {
        if (ValueCount != 1)
            return ProgressEntryErrors.OneValue(field);

        return question.Type switch
        {
            QuestionType.Text when Text is null => ProgressEntryErrors.WrongType(field),
            QuestionType.Text when string.IsNullOrWhiteSpace(Text) => ProgressEntryErrors.EmptyText(field),
            QuestionType.Text when Text.Length > TextMaxLength => ProgressEntryErrors.TextTooLong(field),
            QuestionType.NumericScale when Number is not { } number => ProgressEntryErrors.WrongType(field),
            QuestionType.NumericScale when !question.AcceptsNumber(Number.Value) => ProgressEntryErrors.OutOfRange(field),
            QuestionType.SingleChoice when OptionId is not { } option => ProgressEntryErrors.WrongType(field),
            QuestionType.SingleChoice when question.Options.All(o => o.Id != OptionId) => ProgressEntryErrors.UnknownOption(field),
            QuestionType.Image or QuestionType.Video when MediaAssetId is null => ProgressEntryErrors.WrongType(field),
            _ => null
        };
    }
}
