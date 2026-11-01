namespace MedicalApp.Features.Access.RevokeTreatment;

public class RevokeTreatmentSummary : Summary<RevokeTreatmentEndpoint>
{
    public RevokeTreatmentSummary()
    {
        Summary = "Revoke a treatment relationship";
        Description =
            "Ends a doctor's treatment of a patient and closes that doctor's open plans for the patient, which keep their entries. " +
            "Plans owned by other doctors stay open. If the doctor reviews another doctor's plan through a grant, that access ends at " +
            "the next request while the plan itself stays open. Closure follows the same plan lock as submissions, " +
            "so no submission commits on a closed plan. Repeating the request is safe. Requires the administrator API role.";
    }
}
