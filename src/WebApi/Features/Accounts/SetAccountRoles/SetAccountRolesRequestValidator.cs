using MedicalApp.Common.Authentication;

namespace MedicalApp.Features.Accounts.SetAccountRoles;

public class SetAccountRolesRequestValidator : Validator<SetAccountRolesRequest>
{
    public SetAccountRolesRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();
        RuleFor(v => v.Roles).NotNull();
        RuleForEach(v => v.Roles)
            .Must(ApiRoles.All.Contains)
            .WithMessage($"Roles must be one of: {string.Join(", ", ApiRoles.All)}.");
    }
}
