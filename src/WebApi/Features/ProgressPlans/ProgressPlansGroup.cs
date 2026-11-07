using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.ProgressPlans;

/// <summary>
/// Plan assignment and access. Reading requires an enabled doctor or patient mapping; changes require the doctor policy.
/// Plans outside the caller's scope return 404.
/// </summary>
public class ProgressPlansGroup : Group
{
    public ProgressPlansGroup()
    {
        Configure("progress-plans", ep =>
        {
            ep.Policies(ProgressPolicies.DoctorOrPatient);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
