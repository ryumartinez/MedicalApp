namespace MedicalApp.Common.Domain.ProgressPlans;

public sealed class ProgressPlanSpec : SingleResultSpecification<ProgressPlan>
{
    public static ProgressPlanSpec ById(ProgressPlanId id)
    {
        var spec = new ProgressPlanSpec();
        spec.Query.Where(plan => plan.Id == id);
        return spec;
    }
}
