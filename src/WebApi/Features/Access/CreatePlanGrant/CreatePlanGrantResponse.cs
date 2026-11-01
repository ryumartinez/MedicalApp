namespace MedicalApp.Features.Access.CreatePlanGrant;

public record CreatePlanGrantResponse(Guid Id, Guid PlanId, Guid DoctorId, bool IsActive);
