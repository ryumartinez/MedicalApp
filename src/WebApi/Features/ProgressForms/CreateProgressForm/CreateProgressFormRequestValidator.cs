namespace MedicalApp.Features.ProgressForms.CreateProgressForm;

public class CreateProgressFormRequestValidator : Validator<CreateProgressFormRequest>
{
    public CreateProgressFormRequestValidator()
    {
        RuleFor(v => v.Title).NotEmpty();
        RuleForEach(v => v.Questions).NotNull();
    }
}
