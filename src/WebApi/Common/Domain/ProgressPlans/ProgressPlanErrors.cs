namespace MedicalApp.Common.Domain.ProgressPlans;

public static class ProgressPlanErrors
{
    public static readonly Error NotFound = Error.NotFound("ProgressPlan.NotFound", "Plan is not found");
    public static readonly Error InvalidFrequency = Error.Validation("ProgressPlan.InvalidFrequency", "Frequency must be daily or weekly");
    public static readonly Error InvalidTimeZone = Error.Validation("ProgressPlan.InvalidTimeZone", "TimeZone must be a valid IANA timezone identifier");
    public static readonly Error EndBeforeStart = Error.Validation("ProgressPlan.EndBeforeStart", "EndDate cannot be before StartDate");
    public static readonly Error AlreadyEnded = Error.Conflict("ProgressPlan.AlreadyEnded", "The plan has already ended");
    public static readonly Error Conflict = Error.Conflict("ProgressPlan.Conflict", "The plan changed since it was read. Reload it and try again");
    public static readonly Error NoTreatment = Error.NotFound("ProgressPlan.NoTreatment", "Patient is not found");
    public static readonly Error ReplacedPlanMismatch = Error.Validation("ProgressPlan.ReplacedPlanMismatch", "The replaced plan must be your open plan for the same patient");
}
