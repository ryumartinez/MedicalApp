namespace MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;

/// <param name="PatientId">Required for doctors. Patients may omit it; another patient's ID returns an empty list.</param>
/// <param name="IncludeEnded">Also return ended plans.</param>
public record GetPatientProgressPlansRequest(Guid? PatientId, bool IncludeEnded = false);
