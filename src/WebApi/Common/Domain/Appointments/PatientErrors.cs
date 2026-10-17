namespace MedicalApp.Common.Domain.Appointments;

public static class PatientErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Patient.NotFound",
        "Patient is not found");
}
