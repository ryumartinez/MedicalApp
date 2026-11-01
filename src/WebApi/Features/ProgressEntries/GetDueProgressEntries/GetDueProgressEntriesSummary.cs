namespace MedicalApp.Features.ProgressEntries.GetDueProgressEntries;

public class GetDueProgressEntriesSummary : Summary<GetDueProgressEntriesEndpoint>
{
    public GetDueProgressEntriesSummary()
    {
        Summary = "List periods the caller can still submit";
        Description =
            "Lists started, unsubmitted periods of the caller's open plans, oldest first. The current period is Due; ended periods " +
            "are Missed and can be submitted late. Each item includes its draft and revision when one exists. Future periods are " +
            "never listed, and periods are calculated from the schedule without stored empty entries. Another patient's plan ID " +
            "yields an empty list.";
    }
}
