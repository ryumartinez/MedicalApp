namespace MedicalApp.Features.Accounts.CreateAccount;

public class CreateAccountRequestValidator : Validator<CreateAccountRequest>
{
    public const int UsernameMinLength = 3;
    public const int UsernameMaxLength = 255;
    public const int NameMaxLength = 255;

    public CreateAccountRequestValidator()
    {
        // Lower-case names keep usernames unambiguous for people and make retries match exactly.
        RuleFor(v => v.Username)
            .NotEmpty()
            .Length(UsernameMinLength, UsernameMaxLength)
            .Matches("^[a-z0-9][a-z0-9._-]*$");

        RuleFor(v => v.Email)
            .NotEmpty()
            .EmailAddress()
            .MaximumLength(NameMaxLength);

        RuleFor(v => v.FirstName)
            .NotEmpty()
            .MaximumLength(NameMaxLength);

        RuleFor(v => v.LastName)
            .NotEmpty()
            .MaximumLength(NameMaxLength);
    }
}
