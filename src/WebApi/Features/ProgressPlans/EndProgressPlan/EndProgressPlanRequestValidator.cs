namespace MedicalApp.Features.ProgressPlans.EndProgressPlan;

public class EndProgressPlanRequestValidator : Validator<EndProgressPlanRequest>
{
    public EndProgressPlanRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
