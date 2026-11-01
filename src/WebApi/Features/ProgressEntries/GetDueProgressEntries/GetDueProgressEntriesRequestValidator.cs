namespace MedicalApp.Features.ProgressEntries.GetDueProgressEntries;

public class GetDueProgressEntriesRequestValidator : Validator<GetDueProgressEntriesRequest>
{
    public GetDueProgressEntriesRequestValidator()
    {
        RuleFor(v => v.PlanId).NotEqual(Guid.Empty).When(v => v.PlanId is not null);
    }
}
