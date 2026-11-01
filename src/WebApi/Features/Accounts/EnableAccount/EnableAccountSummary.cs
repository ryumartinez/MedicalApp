namespace MedicalApp.Features.Accounts.EnableAccount;

public class EnableAccountSummary : Summary<EnableAccountEndpoint>
{
    public EnableAccountSummary()
    {
        Summary = "Finish provisioning and enable an account";
        Description =
            "Enables the Keycloak account and then the application mapping. " +
            "Requires at least one linked record, and each doctor or patient role must match a linked record of that type. " +
            "Returns 409 with the remaining steps when provisioning is incomplete. Repeating the request changes nothing. " +
            "Requires the administrator API role.";
    }
}
