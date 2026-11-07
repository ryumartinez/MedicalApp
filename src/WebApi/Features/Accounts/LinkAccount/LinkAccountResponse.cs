namespace MedicalApp.Features.Accounts.LinkAccount;

public record LinkAccountResponse(Guid Id, Guid? DoctorId, Guid? PatientId);
