namespace MedicalApp.Features.Access.CreateTreatment;

public record CreateTreatmentRequest(Guid DoctorId, Guid PatientId);
