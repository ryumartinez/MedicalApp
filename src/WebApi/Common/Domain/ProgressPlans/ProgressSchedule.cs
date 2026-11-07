using MedicalApp.Common.Domain.Base.Interfaces;

namespace MedicalApp.Common.Domain.ProgressPlans;

/// <summary>
/// Daily or weekly local calendar periods in an IANA timezone. Weekly periods are anchored to the start date,
/// and an inclusive end date can shorten the last period.
/// </summary>
/// <remarks>
/// Boundaries are local midnight converted through timezone rules (design decision 10).
/// A skipped midnight resolves to the first valid instant after the gap. A repeated midnight resolves to its earlier occurrence.
/// </remarks>
public sealed record ProgressSchedule : IValueObject
{
    public const int TimeZoneIdMaxLength = 64;

    public ScheduleFrequency Frequency { get; }
    public string TimeZoneId { get; }
    public DateOnly StartDate { get; }
    public DateOnly? EndDate { get; }

    private ProgressSchedule(ScheduleFrequency frequency, string timeZoneId, DateOnly startDate, DateOnly? endDate)
    {
        Frequency = frequency;
        TimeZoneId = timeZoneId;
        StartDate = startDate;
        EndDate = endDate;
    }

    public static ErrorOr<ProgressSchedule> Create(ScheduleFrequency frequency, string? timeZoneId, DateOnly startDate, DateOnly? endDate)
    {
        if (!Enum.IsDefined(frequency))
            return ProgressPlanErrors.InvalidFrequency;
        if (string.IsNullOrWhiteSpace(timeZoneId) || timeZoneId.Length > TimeZoneIdMaxLength || !TryFindIanaZone(timeZoneId, out _))
            return ProgressPlanErrors.InvalidTimeZone;
        if (endDate is { } end && end < startDate)
            return ProgressPlanErrors.EndBeforeStart;
        return new ProgressSchedule(frequency, timeZoneId, startDate, endDate);
    }

    /// <summary>
    /// Rehydrates stored values that were validated when the plan was created.
    /// </summary>
    public static ProgressSchedule FromStored(ScheduleFrequency frequency, string timeZoneId, DateOnly startDate, DateOnly? endDate) =>
        new(frequency, timeZoneId, startDate, endDate);

    public int LengthInDays => Frequency == ScheduleFrequency.Daily ? 1 : 7;

    public TimeZoneInfo TimeZone => TryFindIanaZone(TimeZoneId, out var zone)
        ? zone
        : throw new InvalidOperationException($"Unknown timezone '{TimeZoneId}'.");

    /// <summary>
    /// The scheduled period that starts on <paramref name="startDate"/>, or null when no period starts on that date.
    /// </summary>
    public ReportingPeriod? PeriodStartingOn(DateOnly startDate)
    {
        var offset = startDate.DayNumber - StartDate.DayNumber;
        if (offset < 0 || offset % LengthInDays != 0 || (EndDate is { } end && startDate > end))
            return null;
        return Build(startDate);
    }

    /// <summary>
    /// The scheduled period that contains the local date, or null outside the schedule.
    /// </summary>
    public ReportingPeriod? PeriodContaining(DateOnly date)
    {
        var offset = date.DayNumber - StartDate.DayNumber;
        if (offset < 0 || (EndDate is { } end && date > end))
            return null;
        return Build(StartDate.AddDays(offset - offset % LengthInDays));
    }

    /// <summary>
    /// Every scheduled period that started before <paramref name="until"/>, oldest first.
    /// </summary>
    public IEnumerable<ReportingPeriod> PeriodsStartedBefore(DateTimeOffset until)
    {
        for (var start = StartDate; EndDate is not { } end || start <= end; start = start.AddDays(LengthInDays))
        {
            var period = Build(start);
            if (period.StartUtc >= until)
                yield break;
            yield return period;
        }
    }

    public DateOnly LocalDate(DateTimeOffset instant) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, TimeZone).DateTime);

    private ReportingPeriod Build(DateOnly start)
    {
        var next = start.AddDays(LengthInDays);
        var last = next.AddDays(-1);
        if (EndDate is { } end && last > end)
        {
            last = end;
            next = end.AddDays(1);
        }

        var zone = TimeZone;
        return new ReportingPeriod(start, last, LocalMidnightToUtc(start, zone), LocalMidnightToUtc(next, zone));
    }

    public static DateTimeOffset LocalMidnightToUtc(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);

        if (zone.IsAmbiguousTime(local))
        {
            // Earlier occurrence: the larger offset maps to the earlier instant.
            var offset = zone.GetAmbiguousTimeOffsets(local).Max();
            return new DateTimeOffset(local, offset).ToUniversalTime();
        }

        if (!zone.IsInvalidTime(local))
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();

        // Skipped midnight: find the first instant whose local time is at or after midnight.
        var before = zone.GetUtcOffset(local.AddDays(-1));
        var after = zone.GetUtcOffset(local.AddDays(1));
        var low = new DateTimeOffset(local - after, TimeSpan.Zero);
        var high = new DateTimeOffset(local - before, TimeSpan.Zero);
        if (low > high)
            (low, high) = (high.AddDays(-1), low.AddDays(1));

        while (high - low > TimeSpan.FromTicks(1))
        {
            var middle = low + (high - low) / 2;
            if (TimeZoneInfo.ConvertTime(middle, zone).DateTime >= local)
                high = middle;
            else
                low = middle;
        }

        return high;
    }

    private static bool TryFindIanaZone(string id, out TimeZoneInfo zone)
    {
        // Accept IANA identifiers only. Some platforms also resolve Windows identifiers.
        if (TimeZoneInfo.TryFindSystemTimeZoneById(id, out var found) && found.HasIanaId)
        {
            zone = found;
            return true;
        }

        zone = null!;
        return false;
    }
}
