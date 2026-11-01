namespace MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;

public record GetPatientProgressPlansResponse(IReadOnlyList<PlanDto> Items);
