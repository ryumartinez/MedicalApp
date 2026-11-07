namespace MedicalApp.Features.ProgressEntries.SaveProgressEntryDraft;

public class SaveProgressEntryDraftRequestValidator : Validator<SaveProgressEntryDraftRequest>
{
    public SaveProgressEntryDraftRequestValidator()
    {
        RuleFor(v => v.PlanId).NotEmpty();
        RuleFor(v => v.PeriodStartDate).NotEmpty();
        RuleForEach(v => v.Answers).NotNull().ChildRules(answer => answer.RuleFor(a => a.QuestionId).NotEmpty());
    }
}
