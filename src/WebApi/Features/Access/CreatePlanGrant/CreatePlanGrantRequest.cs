namespace MedicalApp.Features.Access.CreatePlanGrant;

/// <param name="DoctorId">The reviewing doctor. They also need an active treatment relationship with the plan's patient.</param>
public record CreatePlanGrantRequest(Guid PlanId, Guid DoctorId);
