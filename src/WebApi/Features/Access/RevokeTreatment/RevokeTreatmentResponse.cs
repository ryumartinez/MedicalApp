namespace MedicalApp.Features.Access.RevokeTreatment;

/// <param name="ClosedPlanIds">The doctor's plans for the patient that this revocation closed.</param>
public record RevokeTreatmentResponse(Guid Id, bool IsActive, IReadOnlyList<Guid> ClosedPlanIds);
