using MedicalApp.Common.Domain.Base.Interfaces;

namespace MedicalApp.Common.Domain.ProgressPlans;

/// <summary>
/// A run of local dates with UTC boundaries. <see cref="EndDate"/> is inclusive; <see cref="EndUtc"/> is exclusive.
/// </summary>
public sealed record ReportingPeriod(DateOnly StartDate, DateOnly EndDate, DateTimeOffset StartUtc, DateTimeOffset EndUtc) : IValueObject
{
    public bool HasStarted(DateTimeOffset now) => now >= StartUtc;

    public bool HasEnded(DateTimeOffset now) => now >= EndUtc;
}
