namespace MedicalApp.Features.ProgressPlans.EndProgressPlan;

/// <param name="Revision">The plan revision the caller read. A stale revision returns 409.</param>
public record EndProgressPlanRequest(Guid Id, uint Revision);
