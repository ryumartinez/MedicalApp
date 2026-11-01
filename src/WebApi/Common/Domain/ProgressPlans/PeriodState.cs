namespace MedicalApp.Common.Domain.ProgressPlans;

/// <summary>
/// Shared classification used by due entries, submission, history, and summaries.
/// </summary>
public enum PeriodState
{
    /// <summary>Has not started. Never counts as missed.</summary>
    Future = 0,

    /// <summary>Started and not yet ended, without a submitted entry.</summary>
    Due = 1,

    /// <summary>Submitted before the period ended.</summary>
    Completed = 2,

    /// <summary>Submitted after the period ended. Counts as completed and is reported separately.</summary>
    CompletedLate = 3,

    /// <summary>Ended without a submitted entry while the plan was open. A draft does not count.</summary>
    Missed = 4,

    /// <summary>Had not ended when the plan closed and has no submission. Never counts as missed.</summary>
    Closed = 5
}
