using MedicalApp.Common.Domain.ProgressEntries;

namespace MedicalApp.Features.ProgressEntries.GetProgressHistory;

/// <param name="PlanId">One plan. Otherwise PatientId selects all visible plans of a patient; patients may omit both.</param>
/// <param name="From">Earliest period start date, inclusive.</param>
/// <param name="To">Latest period start date, inclusive.</param>
/// <param name="Status">Only drafts or only submitted entries.</param>
public record GetProgressHistoryRequest(
    Guid? PlanId,
    Guid? PatientId,
    DateOnly? From,
    DateOnly? To,
    EntryStatus? Status,
    int Page = 1,
    int PageSize = 20);
