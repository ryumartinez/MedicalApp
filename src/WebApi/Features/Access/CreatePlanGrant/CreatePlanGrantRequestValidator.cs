namespace MedicalApp.Features.Access.CreatePlanGrant;

public class CreatePlanGrantRequestValidator : Validator<CreatePlanGrantRequest>
{
    public CreatePlanGrantRequestValidator()
    {
        RuleFor(v => v.PlanId).NotEmpty();
        RuleFor(v => v.DoctorId).NotEmpty();
    }
}
