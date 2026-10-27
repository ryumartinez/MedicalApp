namespace MedicalApp.Common.Domain.Doctors;

public static class DoctorErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Doctor.NotFound",
        "Doctor is not found");
}
