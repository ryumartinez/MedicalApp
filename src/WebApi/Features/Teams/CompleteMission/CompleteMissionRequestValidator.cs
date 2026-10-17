namespace MedicalApp.Features.Teams.CompleteMission;

public class CompleteMissionRequestValidator : Validator<CompleteMissionRequest>
{
    public CompleteMissionRequestValidator()
    {
        RuleFor(v => v.TeamId)
            .NotEmpty();
    }
}