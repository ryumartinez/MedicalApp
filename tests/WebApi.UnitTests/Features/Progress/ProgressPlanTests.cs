using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.UnitTests.Features.Progress;

public sealed class ProgressPlanTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");

    internal static ProgressFormVersion PublishedVersion(DoctorId doctorId, params QuestionDefinition[] questions)
    {
        var form = ProgressForm.Create(doctorId, "Daily mood", null, questions.Length == 0 ? [ProgressFormTests.Text("note")] : questions).Value;
        return form.Publish(Now).Value;
    }

    private static ProgressPlan Plan(string start = "2026-11-01", string? end = null)
    {
        var doctor = DoctorId.From(Guid.CreateVersion7());
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end)).Value;
        return ProgressPlan.Create(doctor, PatientId.From(Guid.CreateVersion7()), PublishedVersion(doctor), schedule);
    }

    private static ReportingPeriod Period(ProgressPlan plan, string date) => plan.Schedule.PeriodStartingOn(DateOnly.Parse(date))!;

    [Fact]
    public void ClassifiesFutureDueMissedAndSubmittedPeriods()
    {
        var plan = Plan();

        plan.Classify(Period(plan, "2026-11-11"), null, Now).Should().Be(PeriodState.Future);
        plan.Classify(Period(plan, "2026-11-10"), null, Now).Should().Be(PeriodState.Due);
        plan.Classify(Period(plan, "2026-11-09"), null, Now).Should().Be(PeriodState.Missed);
        plan.Classify(Period(plan, "2026-11-09"), DateTimeOffset.Parse("2026-11-09T20:00:00Z"), Now).Should().Be(PeriodState.Completed);
        plan.Classify(Period(plan, "2026-11-09"), DateTimeOffset.Parse("2026-11-10T00:00:00Z"), Now).Should().Be(PeriodState.CompletedLate);
    }

    [Fact]
    public void EndedPlansExcludeUnfinishedAndLaterPeriods()
    {
        var plan = Plan();
        plan.End(DateTimeOffset.Parse("2026-11-08T06:00:00Z"), PlanEndReason.EndedByDoctor).IsError.Should().BeFalse();

        plan.Classify(Period(plan, "2026-11-07"), null, Now).Should().Be(PeriodState.Missed);
        plan.Classify(Period(plan, "2026-11-08"), null, Now).Should().Be(PeriodState.Closed);
        plan.Classify(Period(plan, "2026-11-09"), null, Now).Should().Be(PeriodState.Closed);
        plan.AcceptsEntriesFor(Period(plan, "2026-11-07"), Now).Should().BeFalse();
        plan.End(Now, PlanEndReason.EndedByDoctor).FirstError.Should().Be(ProgressPlanErrors.AlreadyEnded);
    }

    [Fact]
    public void OpenPlansAcceptStartedPeriodsIncludingLateOnes()
    {
        var plan = Plan();

        plan.AcceptsEntriesFor(Period(plan, "2026-11-02"), Now).Should().BeTrue();
        plan.AcceptsEntriesFor(Period(plan, "2026-11-11"), Now).Should().BeFalse();
    }

    [Fact]
    public void ScheduleEndLimitsCountedPeriods()
    {
        var plan = Plan(end: "2026-11-05");

        plan.Schedule.PeriodsStartedBefore(Now).Should().HaveCount(5);
    }

    [Fact]
    public void RejectsAnotherDoctorsVersion()
    {
        var version = PublishedVersion(DoctorId.From(Guid.CreateVersion7()));
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse("2026-11-01"), null).Value;

        var create = () => ProgressPlan.Create(DoctorId.From(Guid.CreateVersion7()), PatientId.From(Guid.CreateVersion7()), version, schedule);

        create.Should().Throw<ArgumentException>();
    }
}
