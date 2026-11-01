namespace MedicalApp.Features.Accounts.GetAccount;

public class GetAccountRequestValidator : Validator<GetAccountRequest>
{
    public GetAccountRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
