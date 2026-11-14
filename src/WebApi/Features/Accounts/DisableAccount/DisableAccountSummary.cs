namespace MedicalApp.Features.Accounts.DisableAccount;

public class DisableAccountSummary : Summary<DisableAccountEndpoint>
{
    public DisableAccountSummary()
    {
        Summary = "Disable an account";
        Description =
            "Disables the application mapping, then disables sign-in for the account, ends its sessions, and revokes its pending authorization codes. " +
            "The next authorization check denies progress access. No new access tokens are issued, " +
            "and access tokens already issued expire within five minutes. Repeating the request is safe. " +
            "Requires the administrator API role.";
    }
}
