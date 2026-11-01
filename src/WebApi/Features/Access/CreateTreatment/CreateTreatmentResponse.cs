namespace MedicalApp.Features.Access.CreateTreatment;

public record CreateTreatmentResponse(Guid Id, Guid DoctorId, Guid PatientId, bool IsActive);
