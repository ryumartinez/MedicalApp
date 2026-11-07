namespace MedicalApp.Features.Access.CreateTreatment;

public class CreateTreatmentSummary : Summary<CreateTreatmentEndpoint>
{
    public CreateTreatmentSummary()
    {
        Summary = "Create or reactivate a treatment relationship";
        Description =
            "Records that a doctor treats a patient. Treatment is required to assign plans and to review any plan for that patient; " +
            "appointments and matching emails never establish it. Repeating the request returns the same relationship, " +
            "and a revoked relationship becomes active again. Plans closed by an earlier revocation stay closed. " +
            "Requires the administrator API role.";
    }
}
