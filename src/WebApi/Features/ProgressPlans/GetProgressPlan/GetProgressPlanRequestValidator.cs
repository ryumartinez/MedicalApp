namespace MedicalApp.Features.ProgressPlans.GetProgressPlan;

public class GetProgressPlanRequestValidator : Validator<GetProgressPlanRequest>
{
    public GetProgressPlanRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
