using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;

namespace MedicalApp.Common.Domain.ProgressPlans;

[ValueObject<Guid>]
public readonly partial struct ProgressPlanGrantId;

/// <summary>
/// Lets another doctor review a plan. Access also requires that doctor's active treatment relationship with the patient.
/// </summary>
public class ProgressPlanGrant : Entity<ProgressPlanGrantId>
{
    public ProgressPlanId PlanId { get; private set; }
    public DoctorId DoctorId { get; private set; }
    public bool IsActive { get; private set; }

    private ProgressPlanGrant() { }

    public static ProgressPlanGrant Create(ProgressPlanId planId, DoctorId doctorId) => new()
    {
        Id = ProgressPlanGrantId.From(Guid.CreateVersion7()),
        PlanId = planId,
        DoctorId = doctorId,
        IsActive = true
    };

    public void Revoke() => IsActive = false;

    public void Reactivate() => IsActive = true;
}
