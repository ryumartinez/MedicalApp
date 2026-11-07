namespace MedicalApp.Features.ProgressPlans.EndProgressPlan;

public class EndProgressPlanSummary : Summary<EndProgressPlanEndpoint>
{
    public EndProgressPlanSummary()
    {
        Summary = "End a plan";
        Description =
            "Closes your plan. Existing drafts and submitted entries stay available, but the patient can no longer change drafts or submit. " +
            "Closure takes the same plan lock as draft saves and submissions: a submission that commits first is kept, " +
            "and none can commit after closure. Periods that had not ended at closure are reported as Closed, not missed. " +
            "Send the revision you read; a stale revision or an already ended plan returns 409.";
    }
}
