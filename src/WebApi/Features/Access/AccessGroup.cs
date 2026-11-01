using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.Access;

/// <summary>
/// Administrator management of treatment relationships and plan grants. Requires the administrator API role.
/// </summary>
public class AccessGroup : Group
{
    public AccessGroup()
    {
        Configure("access", ep =>
        {
            ep.Policies(ProgressPolicies.Administrator);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
