namespace MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;

public class GetPatientProgressPlansSummary : Summary<GetPatientProgressPlansEndpoint>
{
    public GetPatientProgressPlansSummary()
    {
        Summary = "List a patient's plans";
        Description =
            "Lists only plans in the caller's scope: a patient's own plans, a doctor's own plans for patients they treat, " +
            "and other doctors' plans with an active grant and treatment. Plans outside that scope are omitted, so an untreated " +
            "patient yields an empty list. Ended plans are included only when requested.";
    }
}
