namespace MedicalApp.Features.ProgressPlans.GetProgressPlan;

public class GetProgressPlanSummary : Summary<GetProgressPlanEndpoint>
{
    public GetProgressPlanSummary()
    {
        Summary = "Get a plan and its assigned form version";
        Description =
            "Returns the plan's fixed assignment and the published version the patient answers. " +
            "Patients can read their own plans. The owning doctor needs an active treatment relationship with the patient; " +
            "another doctor needs both an active plan grant and an active treatment relationship. " +
            "Every other caller receives 404, as for a missing plan.";
    }
}
