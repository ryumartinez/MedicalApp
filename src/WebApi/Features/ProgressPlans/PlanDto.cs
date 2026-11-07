using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Features.ProgressPlans;

public record PlanDto(
    Guid Id,
    Guid DoctorId,
    Guid PatientId,
    Guid FormId,
    Guid FormVersionId,
    ScheduleFrequency Frequency,
    string TimeZone,
    DateOnly StartDate,
    DateOnly? EndDate,
    DateTimeOffset? EndedAt,
    PlanEndReason? EndReason,
    Guid? ReplacedByPlanId,
    uint Revision)
{
    public static PlanDto From(ProgressPlan plan) => new(
        plan.Id.Value, plan.DoctorId.Value, plan.PatientId.Value, plan.FormId.Value, plan.FormVersionId.Value, plan.Frequency,
        plan.TimeZoneId, plan.StartDate, plan.EndDate, plan.EndedAt, plan.EndReason, plan.ReplacedByPlanId?.Value, plan.Revision);
}
