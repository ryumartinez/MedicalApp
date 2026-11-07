namespace MedicalApp.Common.Domain.ProgressEntries;

public static class ProgressEntryErrors
{
    public static readonly Error NotFound = Error.NotFound("ProgressEntry.NotFound", "Entry is not found");
    public static readonly Error AlreadySubmitted = Error.Conflict("ProgressEntry.AlreadySubmitted", "Submitted entries cannot change");
    public static readonly Error Conflict = Error.Conflict("ProgressEntry.Conflict", "The entry changed since it was read. Reload it and try again");
    public static readonly Error PeriodNotScheduled = Error.Validation("ProgressEntry.PeriodNotScheduled", "No reporting period of this plan starts on that date");
    public static readonly Error PeriodNotStarted = Error.Validation("ProgressEntry.PeriodNotStarted", "The reporting period has not started");
    public static readonly Error PlanClosed = Error.Conflict("ProgressEntry.PlanClosed", "The plan has ended and accepts no changes");

    public static Error OneValue(string field) => Error.Validation("ProgressEntry.OneValue", $"{field} must have exactly one value");
    public static Error WrongType(string field) => Error.Validation("ProgressEntry.WrongType", $"{field} has a value of the wrong type for its question");
    public static Error EmptyText(string field) => Error.Validation("ProgressEntry.EmptyText", $"{field} is empty. Omit the answer instead");
    public static Error TextTooLong(string field) => Error.Validation("ProgressEntry.TextTooLong", $"{field} can have at most {ProgressAnswer.TextMaxLength} characters");
    public static Error OutOfRange(string field) => Error.Validation("ProgressEntry.OutOfRange", $"{field} is outside the question's scale or step");
    public static Error UnknownOption(string field) => Error.Validation("ProgressEntry.UnknownOption", $"{field} references an option that the version does not define");
    public static Error UnknownQuestion(string field) => Error.Validation("ProgressEntry.UnknownQuestion", $"{field} references a question that the version does not define");
    public static Error DuplicateAnswer(string field) => Error.Validation("ProgressEntry.DuplicateAnswer", $"{field} answers a question that already has an answer");
    public static Error RequiredMissing(string key) => Error.Validation("ProgressEntry.RequiredMissing", $"Question '{key}' requires an answer");
    public static Error AttachmentUnavailable(string field) => Error.Validation("ProgressEntry.AttachmentUnavailable", $"{field} references media that is not ready for this entry and question");
}
