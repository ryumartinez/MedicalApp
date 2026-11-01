namespace MedicalApp.Features.ProgressEntries.GetProgressHistory;

public class GetProgressHistoryRequestValidator : Validator<GetProgressHistoryRequest>
{
    public const int MaxPageSize = 100;

    public GetProgressHistoryRequestValidator()
    {
        RuleFor(v => v.Page).GreaterThanOrEqualTo(1);
        RuleFor(v => v.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(v => v.To).GreaterThanOrEqualTo(v => v.From).When(v => v.From is not null && v.To is not null);
        RuleFor(v => v.Status).IsInEnum().When(v => v.Status is not null);
    }
}
