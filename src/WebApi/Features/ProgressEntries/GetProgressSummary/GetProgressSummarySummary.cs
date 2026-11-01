namespace MedicalApp.Features.ProgressEntries.GetProgressSummary;

public class GetProgressSummarySummary : Summary<GetProgressSummaryEndpoint>
{
    public GetProgressSummarySummary()
    {
        Summary = "Get completion counts and numeric trends";
        Description =
            "Counts each plan's started periods with the same rules as due lists and submission: Completed includes late " +
            "submissions, which CompletedLate also reports; Missed counts ended periods without a submission while the plan was open, " +
            "and a saved draft does not prevent a miss; Due is the current period; Closed counts periods that had not ended when the " +
            "plan closed, which are never missed. Future periods are counted only for plans with an end date, and never as missed. " +
            "Numeric trends use submitted answers and are grouped by published version and question, with each version's scale and " +
            "labels, so a changed scale or meaning stays a separate series. Doctors must specify a plan or patient in their scope.";
    }
}
