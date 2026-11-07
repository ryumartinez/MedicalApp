namespace MedicalApp.Features.ProgressPlans.AssignProgressPlan;

public class AssignProgressPlanRequestValidator : Validator<AssignProgressPlanRequest>
{
    public AssignProgressPlanRequestValidator()
    {
        RuleFor(v => v.PatientId).NotEmpty();
        RuleFor(v => v.FormVersionId).NotEmpty();
        RuleFor(v => v.Frequency).IsInEnum();
        RuleFor(v => v.TimeZone).NotEmpty();
        RuleFor(v => v.StartDate).NotEmpty();
        RuleFor(v => v.ReplacesPlanId).NotEqual(Guid.Empty).When(v => v.ReplacesPlanId is not null);
    }
}
