namespace MedicalApp.Features.ProgressForms.GetProgressForm;

public class GetProgressFormRequestValidator : Validator<GetProgressFormRequest>
{
    public GetProgressFormRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
