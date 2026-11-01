namespace MedicalApp.Features.Access.CreatePlanGrant;

public class CreatePlanGrantSummary : Summary<CreatePlanGrantEndpoint>
{
    public CreatePlanGrantSummary()
    {
        Summary = "Grant a doctor review access to a plan";
        Description =
            "Lets another doctor read a plan, its entries, history, summaries, and media. A grant never replaces treatment: " +
            "the reviewing doctor must also actively treat the plan's patient. Repeating the request returns the same grant, " +
            "and a revoked grant becomes active again. Requires the administrator API role.";
    }
}
