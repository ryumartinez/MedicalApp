namespace MedicalApp.Features.Accounts.GetAccount;

public class GetAccountSummary : Summary<GetAccountEndpoint>
{
    public GetAccountSummary()
    {
        Summary = "Get an account's provisioning state";
        Description =
            "Returns the application mapping, the sign-in account (including whether it has a password), and the provisioning steps that remain. " +
            "Use the pending steps to recover an incomplete provisioning: link records, set roles, then enable the account. " +
            "Requires the administrator API role.";
    }
}
