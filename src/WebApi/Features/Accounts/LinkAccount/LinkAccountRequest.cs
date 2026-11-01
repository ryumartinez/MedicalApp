namespace MedicalApp.Features.Accounts.LinkAccount;

/// <summary>
/// Links an existing record by ID, or creates a new record. A retry never creates a second record.
/// </summary>
public record LinkAccountRequest(
    Guid Id,
    Guid? DoctorId,
    Guid? PatientId,
    LinkAccountRequest.NewDoctorDto? NewDoctor,
    LinkAccountRequest.NewPatientDto? NewPatient)
{
    public record NewDoctorDto(string FullName, string Specialty);

    public record NewPatientDto(string FullName, string Email, string Phone);
}
