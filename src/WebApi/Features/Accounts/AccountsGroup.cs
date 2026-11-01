using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.Accounts;

/// <summary>
/// Administrator account provisioning. Requires the <c>administrator</c> role on the API client.
/// Keycloak administrative roles alone grant no access.
/// </summary>
public class AccountsGroup : Group
{
    public AccountsGroup()
    {
        Configure("accounts", ep =>
        {
            ep.Policies(ProgressPolicies.Administrator);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
