namespace MedicalApp.Features.ProgressEntries.GetDueProgressEntries;

/// <param name="PlanId">Limit to one plan. By default, every open plan of the caller.</param>
public record GetDueProgressEntriesRequest(Guid? PlanId);
