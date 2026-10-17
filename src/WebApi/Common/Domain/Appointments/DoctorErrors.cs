namespace MedicalApp.Common.Domain.Appointments;

public static class DoctorErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Doctor.NotFound",
        "Doctor is not found");
}
