using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Features.ProgressPlans.AssignProgressPlan;

public class AssignProgressPlanSummary : Summary<AssignProgressPlanEndpoint>
{
    public AssignProgressPlanSummary()
    {
        Summary = "Assign a published form version to a patient";
        Description =
            "Creates a plan for one of your published versions on a daily or weekly schedule in an IANA timezone. " +
            "Weekly periods start on the plan's start date; an inclusive end date can shorten the last period. " +
            "Requires an active treatment relationship with the patient; appointments do not grant access. " +
            "A plan's version, schedule, and timezone never change. To change an assignment, send ReplacesPlanId: " +
            "the earlier plan ends with reason Replaced, keeps its entries, and stays tied to its own version. " +
            "Returns 404 for versions you do not own and for patients you do not treat, and 400 for invalid schedules.";

        ExampleRequest = new AssignProgressPlanRequest(Guid.Parse("01967a92-2ff8-7a00-8000-000000000201"),
            Guid.Parse("01967a92-2ff8-7a00-8000-000000000501"), ScheduleFrequency.Weekly, "America/Santiago",
            new DateOnly(2026, 11, 4), new DateOnly(2027, 1, 31), null);
    }
}
