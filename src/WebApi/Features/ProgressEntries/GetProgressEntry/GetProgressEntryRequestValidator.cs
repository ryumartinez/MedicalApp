namespace MedicalApp.Features.ProgressEntries.GetProgressEntry;

public class GetProgressEntryRequestValidator : Validator<GetProgressEntryRequest>
{
    public GetProgressEntryRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
