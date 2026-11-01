using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.UnitTests.Features.Progress;

public sealed class ProgressScheduleTests
{
    private static ProgressSchedule Schedule(ScheduleFrequency frequency, string zone, string start, string? end = null) =>
        ProgressSchedule.Create(frequency, zone, DateOnly.Parse(start), end is null ? null : DateOnly.Parse(end)).Value;

    [Fact]
    public void WeeklyPeriodsAnchorToStartDate()
    {
        // 2026-11-04 is a Wednesday.
        var schedule = Schedule(ScheduleFrequency.Weekly, "America/Sao_Paulo", "2026-11-04");

        var first = schedule.PeriodContaining(DateOnly.Parse("2026-11-08"))!;

        first.StartDate.Should().Be(DateOnly.Parse("2026-11-04"));
        first.EndDate.Should().Be(DateOnly.Parse("2026-11-10"));
        schedule.PeriodStartingOn(DateOnly.Parse("2026-11-11"))!.EndDate.Should().Be(DateOnly.Parse("2026-11-17"));
        schedule.PeriodStartingOn(DateOnly.Parse("2026-11-09")).Should().BeNull();
        schedule.PeriodStartingOn(DateOnly.Parse("2026-10-28")).Should().BeNull();
    }

    [Fact]
    public void InclusiveEndDateShortensFinalPeriod()
    {
        var schedule = Schedule(ScheduleFrequency.Weekly, "UTC", "2026-11-04", "2026-11-13");

        var last = schedule.PeriodStartingOn(DateOnly.Parse("2026-11-11"))!;

        last.EndDate.Should().Be(DateOnly.Parse("2026-11-13"));
        last.EndUtc.Should().Be(DateTimeOffset.Parse("2026-11-14T00:00:00Z"));
        schedule.PeriodContaining(DateOnly.Parse("2026-11-14")).Should().BeNull();
        schedule.PeriodStartingOn(DateOnly.Parse("2026-11-18")).Should().BeNull();
        schedule.PeriodsStartedBefore(DateTimeOffset.MaxValue).Should().HaveCount(2);
    }

    [Fact]
    public void DailyPeriodsCoverOneLocalDate()
    {
        var schedule = Schedule(ScheduleFrequency.Daily, "America/Sao_Paulo", "2026-06-01");

        var period = schedule.PeriodStartingOn(DateOnly.Parse("2026-06-01"))!;

        period.EndDate.Should().Be(period.StartDate);
        period.StartUtc.Should().Be(DateTimeOffset.Parse("2026-06-01T03:00:00Z"));
        period.EndUtc.Should().Be(DateTimeOffset.Parse("2026-06-02T03:00:00Z"));
    }

    // The examples recorded in design decision 10.
    [Theory]
    [InlineData("America/Santiago", "2026-09-06", "2026-09-06T04:00:00Z")]
    [InlineData("America/Havana", "2026-03-08", "2026-03-08T05:00:00Z")]
    [InlineData("Atlantic/Azores", "2026-10-25", "2026-10-25T00:00:00Z")]
    [InlineData("America/Sao_Paulo", "2026-06-01", "2026-06-01T03:00:00Z")]
    public void ResolvesSkippedAndRepeatedMidnightDeterministically(string zone, string date, string expectedUtc)
    {
        var utc = ProgressSchedule.LocalMidnightToUtc(DateOnly.Parse(date), TimeZoneInfo.FindSystemTimeZoneById(zone));

        utc.Should().Be(DateTimeOffset.Parse(expectedUtc));
    }

    [Fact]
    public void DaylightSavingChangesPeriodLengthWithoutChangingLocalDates()
    {
        var gap = Schedule(ScheduleFrequency.Daily, "America/Santiago", "2026-09-01").PeriodStartingOn(DateOnly.Parse("2026-09-06"))!;
        var overlap = Schedule(ScheduleFrequency.Daily, "Atlantic/Azores", "2026-10-01").PeriodStartingOn(DateOnly.Parse("2026-10-25"))!;
        var week = Schedule(ScheduleFrequency.Weekly, "America/New_York", "2026-11-01").PeriodStartingOn(DateOnly.Parse("2026-11-01"))!;

        (gap.EndUtc - gap.StartUtc).Should().Be(TimeSpan.FromHours(23));
        (overlap.EndUtc - overlap.StartUtc).Should().Be(TimeSpan.FromHours(25));
        week.StartUtc.Should().Be(DateTimeOffset.Parse("2026-11-01T04:00:00Z"));
        week.EndUtc.Should().Be(DateTimeOffset.Parse("2026-11-08T05:00:00Z"));
        (week.EndUtc - week.StartUtc).Should().Be(TimeSpan.FromHours(169));
    }

    [Fact]
    public void AdjacentPeriodsShareBoundaries()
    {
        var schedule = Schedule(ScheduleFrequency.Daily, "America/Santiago", "2026-09-01");

        var periods = schedule.PeriodsStartedBefore(DateTimeOffset.Parse("2026-09-10T00:00:00Z")).ToList();

        periods.Zip(periods.Skip(1)).Should().OnlyContain(pair => pair.First.EndUtc == pair.Second.StartUtc);
    }

    [Theory]
    [InlineData("Mars/Olympus")]
    [InlineData("")]
    [InlineData("Pacific Standard Time")]
    public void RejectsNonIanaTimezones(string zone)
    {
        ProgressSchedule.Create(ScheduleFrequency.Daily, zone, DateOnly.Parse("2026-11-01"), null)
            .FirstError.Should().Be(ProgressPlanErrors.InvalidTimeZone);
    }

    [Fact]
    public void RejectsEndBeforeStart()
    {
        ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse("2026-11-02"), DateOnly.Parse("2026-11-01"))
            .FirstError.Should().Be(ProgressPlanErrors.EndBeforeStart);
    }
}
