using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Common.Authorization;

/// <summary>
/// Resource checks shared by progress endpoints. Every check reads current database state, so disabled mappings,
/// revoked treatment, and revoked grants take effect at the next request. Appointments never establish access.
/// </summary>
public sealed class ProgressAuthorizationService(ApplicationDbContext dbContext)
{
    public Task<bool> HasActiveTreatmentAsync(DoctorId doctorId, PatientId patientId, CancellationToken cancellationToken) =>
        dbContext.DoctorPatientRelationships
            .WithSpecification(DoctorPatientRelationshipSpec.ActiveByDoctorAndPatient(doctorId, patientId))
            .AnyAsync(cancellationToken);

    /// <summary>
    /// Patients the doctor currently treats, for composing scoped collection queries.
    /// </summary>
    public IQueryable<PatientId> TreatedPatientIds(DoctorId doctorId) =>
        dbContext.DoctorPatientRelationships
            .Where(relationship => relationship.DoctorId == doctorId && relationship.IsActive)
            .Select(relationship => relationship.PatientId);

    /// <summary>
    /// Plans a doctor can review: their own plans, or plans with an active grant, and in both cases only for patients they treat.
    /// </summary>
    public IQueryable<ProgressPlan> PlansVisibleToDoctor(DoctorId doctorId)
    {
        var treated = TreatedPatientIds(doctorId);
        var granted = dbContext.ProgressPlanGrants
            .Where(grant => grant.DoctorId == doctorId && grant.IsActive)
            .Select(grant => grant.PlanId);
        return dbContext.ProgressPlans
            .Where(plan => treated.Contains(plan.PatientId) && (plan.DoctorId == doctorId || granted.Contains(plan.Id)));
    }

    /// <summary>
    /// Plans the caller can read in any of their roles. A caller with both mappings sees the union.
    /// </summary>
    public IQueryable<ProgressPlan> PlansVisibleTo(CurrentIdentity identity)
    {
        IQueryable<ProgressPlan>? visible = null;
        if (identity.DoctorId is { } doctorId)
            visible = PlansVisibleToDoctor(doctorId);
        if (identity.PatientId is { } patientId)
        {
            var own = dbContext.ProgressPlans.Where(plan => plan.PatientId == patientId);
            visible = visible is null ? own : visible.Union(own);
        }

        return visible ?? dbContext.ProgressPlans.Where(_ => false);
    }

    public Task<bool> CanReadPlanAsync(CurrentIdentity identity, ProgressPlanId planId, CancellationToken cancellationToken) =>
        PlansVisibleTo(identity).AnyAsync(plan => plan.Id == planId, cancellationToken);

    /// <summary>
    /// Only the plan's owner with an active treatment relationship can change or end it.
    /// </summary>
    public async Task<bool> CanManagePlanAsync(DoctorId doctorId, ProgressPlan plan, CancellationToken cancellationToken) =>
        plan.DoctorId == doctorId && await HasActiveTreatmentAsync(doctorId, plan.PatientId, cancellationToken);
}
