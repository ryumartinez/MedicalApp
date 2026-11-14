namespace MedicalApp.Common.Persistence;

/// <summary>
/// Returns the current time truncated to whole microseconds, the precision PostgreSQL stores for
/// <c>timestamp with time zone</c>. .NET clocks tick every 100 nanoseconds, so without this a value
/// returned from memory differs from the same value read back from the database.
/// </summary>
public sealed class DatabaseTimeProvider(TimeProvider inner) : TimeProvider
{
    public static DatabaseTimeProvider Instance { get; } = new(TimeProvider.System);

    public override DateTimeOffset GetUtcNow() => Truncate(inner.GetUtcNow());

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long TimestampFrequency => inner.TimestampFrequency;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        inner.CreateTimer(callback, state, dueTime, period);

    public static DateTimeOffset Truncate(DateTimeOffset value) =>
        value.AddTicks(-(value.Ticks % TimeSpan.TicksPerMicrosecond));
}
