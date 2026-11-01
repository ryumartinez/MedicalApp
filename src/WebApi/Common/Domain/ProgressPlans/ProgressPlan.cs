using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Common.Domain.ProgressPlans;

[ValueObject<Guid>]
public readonly partial struct ProgressPlanId;

/// <summary>
/// Assigns one published form version to one patient on a fixed schedule.
/// The assignment never changes. A changed assignment uses a new plan and ends this one.
/// </summary>
public class ProgressPlan : Entity<ProgressPlanId>
{
    public DoctorId DoctorId { get; private set; }
    public PatientId PatientId { get; private set; }
    public ProgressFormId FormId { get; private set; }
    public ProgressFormVersionId FormVersionId { get; private set; }
    public ScheduleFrequency Frequency { get; private set; }
    public string TimeZoneId { get; private set; } = null!;
    public DateOnly StartDate { get; private set; }
    public DateOnly? EndDate { get; private set; }
    public DateTimeOffset? EndedAt { get; private set; }
    public PlanEndReason? EndReason { get; private set; }
    public ProgressPlanId? ReplacedByPlanId { get; private set; }
    public uint Revision { get; private set; }

    private ProgressPlan() { }

    public static ProgressPlan Create(DoctorId doctorId, PatientId patientId, ProgressFormVersion version, ProgressSchedule schedule)
    {
        if (version.DoctorId != doctorId)
            throw new ArgumentException("Doctors can assign only their own published versions.", nameof(version));

        return new ProgressPlan
        {
            Id = ProgressPlanId.From(Guid.CreateVersion7()),
            DoctorId = doctorId,
            PatientId = patientId,
            FormId = version.FormId,
            FormVersionId = version.Id,
            Frequency = schedule.Frequency,
            TimeZoneId = schedule.TimeZoneId,
            StartDate = schedule.StartDate,
            EndDate = schedule.EndDate
        };
    }

    public ProgressSchedule Schedule => ProgressSchedule.FromStored(Frequency, TimeZoneId, StartDate, EndDate);

    /// <summary>
    /// An open plan accepts draft changes and submissions, including late ones.
    /// </summary>
    public bool IsOpen => EndedAt is null;

    public ErrorOr<Success> End(DateTimeOffset now, PlanEndReason reason, ProgressPlanId? replacedBy = null)
    {
        if (!IsOpen)
            return ProgressPlanErrors.AlreadyEnded;
        EndedAt = now;
        EndReason = reason;
        ReplacedByPlanId = replacedBy;
        return Result.Success;
    }

    /// <summary>
    /// The single rule for period completion, used by due lists, submission, history, and summaries.
    /// </summary>
    public PeriodState Classify(ReportingPeriod period, DateTimeOffset? submittedAt, DateTimeOffset now)
    {
        if (submittedAt is { } submitted)
            return submitted < period.EndUtc ? PeriodState.Completed : PeriodState.CompletedLate;
        if (!period.HasStarted(now))
            return PeriodState.Future;
        if (EndedAt is { } ended && period.EndUtc > ended)
            return PeriodState.Closed;
        return period.HasEnded(now) ? PeriodState.Missed : PeriodState.Due;
    }

    /// <summary>
    /// A patient can save or submit an entry for a period that has started while the plan is open.
    /// </summary>
    public bool AcceptsEntriesFor(ReportingPeriod period, DateTimeOffset now) => IsOpen && period.HasStarted(now);
}
