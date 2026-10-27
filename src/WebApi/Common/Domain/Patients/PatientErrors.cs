namespace MedicalApp.Common.Domain.Patients;

public static class PatientErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Patient.NotFound",
        "Patient is not found");
}
