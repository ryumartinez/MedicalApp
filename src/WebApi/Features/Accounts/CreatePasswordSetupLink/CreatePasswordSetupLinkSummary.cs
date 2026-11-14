namespace MedicalApp.Features.Accounts.CreatePasswordSetupLink;

public class CreatePasswordSetupLinkSummary : Summary<CreatePasswordSetupLinkEndpoint>
{
    public CreatePasswordSetupLinkSummary()
    {
        Summary = "Create a password setup link for an account";
        Description =
            "Returns a single-use link that lets the account holder choose their password, and when it expires. " +
            "Give the link to the account holder through a trusted channel. A new link replaces earlier unused links " +
            "and ends the account's sign-in sessions. The response never contains a password. Requires the administrator API role.";
    }
}
