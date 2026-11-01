using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Features.ProgressEntries.GetDueProgressEntries;

public record GetDueProgressEntriesResponse(IReadOnlyList<GetDueProgressEntriesResponse.DuePeriodDto> Items)
{
    /// <param name="State">Due for the current period, Missed for an ended one that can still be submitted late.</param>
    /// <param name="EntryId">The period's draft, when one exists.</param>
    public record DuePeriodDto(
        Guid PlanId,
        DateOnly PeriodStartDate,
        DateOnly PeriodEndDate,
        DateTimeOffset PeriodStartUtc,
        DateTimeOffset PeriodEndUtc,
        PeriodState State,
        Guid? EntryId,
        EntryStatus? EntryStatus,
        uint? Revision);
}
