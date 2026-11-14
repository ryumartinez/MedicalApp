using MedicalApp.Common.Persistence;

namespace MedicalApp.IntegrationTests.Common.Utilities;

/// <summary>
/// The real clock shifted by an adjustable offset, at the database's microsecond precision like
/// <see cref="DatabaseTimeProvider"/>. Tests move time forward without waiting.
/// </summary>
public sealed class TestTimeProvider : TimeProvider
{
    private long _offsetTicks;

    public TimeSpan Offset
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _offsetTicks));
        set => Interlocked.Exchange(ref _offsetTicks, value.Ticks);
    }

    public override DateTimeOffset GetUtcNow() => DatabaseTimeProvider.Truncate(base.GetUtcNow() + Offset);
}
