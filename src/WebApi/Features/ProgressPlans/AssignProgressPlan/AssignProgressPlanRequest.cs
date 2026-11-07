using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Features.ProgressPlans.AssignProgressPlan;

/// <param name="TimeZone">IANA timezone identifier, such as America/Santiago.</param>
/// <param name="EndDate">Optional inclusive last local date.</param>
/// <param name="ReplacesPlanId">An open plan of yours for the same patient. It ends when this plan is created.</param>
public record AssignProgressPlanRequest(
    Guid PatientId,
    Guid FormVersionId,
    ScheduleFrequency Frequency,
    string TimeZone,
    DateOnly StartDate,
    DateOnly? EndDate,
    Guid? ReplacesPlanId);
