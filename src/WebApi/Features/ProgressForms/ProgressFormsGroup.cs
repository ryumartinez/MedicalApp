using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.ProgressForms;

/// <summary>
/// Doctor-owned forms. Requires the doctor API role and an enabled doctor mapping.
/// Forms outside the caller's ownership return 404.
/// </summary>
public class ProgressFormsGroup : Group
{
    public ProgressFormsGroup()
    {
        Configure("progress-forms", ep =>
        {
            ep.Policies(ProgressPolicies.Doctor);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
