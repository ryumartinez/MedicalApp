namespace MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;

public class UpdateProgressFormDraftRequestValidator : Validator<UpdateProgressFormDraftRequest>
{
    public UpdateProgressFormDraftRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
        RuleFor(v => v.Title).NotEmpty();
        RuleForEach(v => v.Questions).NotNull();
    }
}
