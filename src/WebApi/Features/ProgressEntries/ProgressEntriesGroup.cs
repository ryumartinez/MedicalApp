using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.ProgressEntries;

/// <summary>
/// Entries, history, and summaries. Reading requires an enabled doctor or patient mapping; changes require the patient policy.
/// Entries outside the caller's scope return 404.
/// </summary>
public class ProgressEntriesGroup : Group
{
    public ProgressEntriesGroup()
    {
        Configure("progress-entries", ep =>
        {
            ep.Policies(ProgressPolicies.DoctorOrPatient);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
