namespace MedicalApp.Features.Accounts.SetAccountRoles;

public record SetAccountRolesRequest(Guid Id, IReadOnlyList<string> Roles);
