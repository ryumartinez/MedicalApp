namespace MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;

public class GetPatientProgressPlansRequestValidator : Validator<GetPatientProgressPlansRequest>
{
    public GetPatientProgressPlansRequestValidator()
    {
        RuleFor(v => v.PatientId).NotEqual(Guid.Empty).When(v => v.PatientId is not null);
    }
}
