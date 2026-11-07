namespace MedicalApp.Features.ProgressForms.PublishProgressForm;

public class PublishProgressFormRequestValidator : Validator<PublishProgressFormRequest>
{
    public PublishProgressFormRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
    }
}
