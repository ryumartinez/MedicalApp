namespace MedicalApp.Features.Access.RevokePlanGrant;

public class RevokePlanGrantSummary : Summary<RevokePlanGrantEndpoint>
{
    public RevokePlanGrantSummary()
    {
        Summary = "Revoke a plan grant";
        Description =
            "Ends the reviewing doctor's access to the plan at their next request. The plan and its entries are unchanged. " +
            "Repeating the request is safe. Requires the administrator API role.";
    }
}
