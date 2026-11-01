using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries;

/// <summary>
/// Resolves the plans a history or summary request covers, always within the caller's access scope.
/// </summary>
public static class ProgressScope
{
    public static readonly Error PlanOrPatientRequired =
        Error.Validation("ProgressEntry.PlanOrPatientRequired", "Specify PlanId or PatientId");

    public static ErrorOr<IQueryable<ProgressPlan>> Plans(ProgressAuthorizationService authorization, CurrentIdentity identity,
        Guid? planId, Guid? patientId)
    {
        var plans = authorization.PlansVisibleTo(identity);
        if (planId is { } plan)
        {
            var id = ProgressPlanId.From(plan);
            return ErrorOrFactory.From(plans.Where(p => p.Id == id));
        }
        if (patientId is { } patient)
        {
            var id = PatientId.From(patient);
            return ErrorOrFactory.From(plans.Where(p => p.PatientId == id));
        }
        if (identity.PatientId is { } own)
            return ErrorOrFactory.From(plans.Where(p => p.PatientId == own));
        return PlanOrPatientRequired;
    }
}
