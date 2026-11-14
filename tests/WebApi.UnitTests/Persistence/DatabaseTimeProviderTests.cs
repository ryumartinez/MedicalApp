using MedicalApp.Common.Persistence;

namespace MedicalApp.UnitTests.Persistence;

public sealed class DatabaseTimeProviderTests
{
    [Fact]
    public void TruncatesToWholeMicroseconds()
    {
        var value = new DateTimeOffset(2026, 11, 14, 17, 28, 10, TimeSpan.Zero).AddTicks(4_321_792);

        DatabaseTimeProvider.Truncate(value).Should().Be(
            new DateTimeOffset(2026, 11, 14, 17, 28, 10, TimeSpan.Zero).AddTicks(4_321_790));
    }

    [Fact]
    public void KeepsValuesAlreadyAtMicrosecondPrecision()
    {
        var value = new DateTimeOffset(2026, 11, 14, 17, 28, 10, TimeSpan.FromHours(-3)).AddTicks(4_321_790);

        DatabaseTimeProvider.Truncate(value).Should().Be(value);
        DatabaseTimeProvider.Truncate(value).Offset.Should().Be(TimeSpan.FromHours(-3));
    }

    [Fact]
    public void ReturnsCurrentTimeAtMicrosecondPrecision()
    {
        var inner = new FixedTimeProvider(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero).AddTicks(7));

        var now = new DatabaseTimeProvider(inner).GetUtcNow();

        now.Should().Be(new DateTimeOffset(2026, 11, 14, 0, 0, 0, TimeSpan.Zero));
        (now.Ticks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
    }

    [Fact]
    public void InstanceHasMicrosecondPrecision()
    {
        for (var i = 0; i < 100; i++)
            (DatabaseTimeProvider.Instance.GetUtcNow().Ticks % TimeSpan.TicksPerMicrosecond).Should().Be(0);
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
