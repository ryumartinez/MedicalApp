namespace MedicalApp.Features.ProgressEntries.GetProgressSummary;

/// <param name="PlanId">One plan. Otherwise PatientId covers all visible plans of a patient; patients may omit both.</param>
public record GetProgressSummaryRequest(Guid? PlanId, Guid? PatientId);
