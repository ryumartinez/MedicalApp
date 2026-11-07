namespace MedicalApp.Common.Domain.ProgressForms;

public static class ProgressFormErrors
{
    public static readonly Error NotFound = Error.NotFound("ProgressForm.NotFound", "Form is not found");
    public static readonly Error VersionNotFound = Error.NotFound("ProgressForm.VersionNotFound", "Published form version is not found");
    public static readonly Error Conflict = Error.Conflict("ProgressForm.Conflict", "The form changed since it was read. Reload it and try again");
    public static readonly Error TitleRequired = Error.Validation("ProgressForm.TitleRequired", "Title is required");
    public static readonly Error NoQuestions = Error.Validation("ProgressForm.NoQuestions", "A published form needs at least one question");
    public static readonly Error TooManyQuestions = Error.Validation("ProgressForm.TooManyQuestions", $"A form can have at most {ProgressForm.MaxQuestions} questions");
    public static readonly Error DuplicateKey = Error.Validation("ProgressForm.DuplicateKey", "Question keys must be unique within a form");

    public static Error TooLong(string field, int max) => Error.Validation("ProgressForm.TooLong", $"{field} can have at most {max} characters");
    public static Error InvalidKey(string field) => Error.Validation("ProgressForm.InvalidKey", $"{field}.Key must be lowercase letters, digits, '_' or '-', start with a letter, and have at most {QuestionDefinition.KeyMaxLength} characters");
    public static Error InvalidType(string field) => Error.Validation("ProgressForm.InvalidType", $"{field}.Type is not a supported question type");
    public static Error LabelRequired(string field) => Error.Validation("ProgressForm.LabelRequired", $"{field} needs a label");
    public static Error InvalidScale(string field) => Error.Validation("ProgressForm.InvalidScale", $"{field} needs an integer minimum lower than its maximum");
    public static Error InvalidStep(string field) => Error.Validation("ProgressForm.InvalidStep", $"{field}.Step must be positive and divide the scale range");
    public static Error TooFewOptions(string field) => Error.Validation("ProgressForm.TooFewOptions", $"{field} needs at least {QuestionDefinition.MinOptions} options");
    public static Error TooManyOptions(string field) => Error.Validation("ProgressForm.TooManyOptions", $"{field} can have at most {QuestionDefinition.MaxOptions} options");
    public static Error DuplicateOption(string field) => Error.Validation("ProgressForm.DuplicateOption", $"{field} has duplicate option IDs");
    public static Error UnsupportedContentType(string field) => Error.Validation("ProgressForm.UnsupportedContentType", $"{field} needs supported content types of its media kind");
    public static Error MembersDoNotMatchType(string field) => Error.Validation("ProgressForm.MembersDoNotMatchType", $"{field} sets rules that do not apply to its type");
}
