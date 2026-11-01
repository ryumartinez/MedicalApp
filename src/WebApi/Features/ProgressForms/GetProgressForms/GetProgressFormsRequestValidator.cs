namespace MedicalApp.Features.ProgressForms.GetProgressForms;

public class GetProgressFormsRequestValidator : Validator<GetProgressFormsRequest>
{
    public const int MaxPageSize = 100;

    public GetProgressFormsRequestValidator()
    {
        RuleFor(v => v.Page).GreaterThanOrEqualTo(1);
        RuleFor(v => v.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(v => v.Search).MaximumLength(200);
    }
}
