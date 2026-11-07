namespace MedicalApp.Features.ProgressEntries.GetProgressSummary;

public class GetProgressSummaryRequestValidator : Validator<GetProgressSummaryRequest>
{
    public GetProgressSummaryRequestValidator()
    {
        RuleFor(v => v.PlanId).NotEqual(Guid.Empty).When(v => v.PlanId is not null);
        RuleFor(v => v.PatientId).NotEqual(Guid.Empty).When(v => v.PatientId is not null);
    }
}
