namespace MedicalApp.Features.Accounts.DisableAccount;

public class DisableAccountSummary : Summary<DisableAccountEndpoint>
{
    public DisableAccountSummary()
    {
        Summary = "Disable an account";
        Description =
            "Disables the application mapping, then disables the Keycloak account and ends its sessions. " +
            "The next authorization check denies progress access. Refresh tokens cannot issue new access tokens, " +
            "and access tokens already issued expire within five minutes. Repeating the request is safe. " +
            "Requires the administrator API role.";
    }
}
