namespace MedicalApp.Features.ProgressEntries.GetProgressHistory;

public class GetProgressHistorySummary : Summary<GetProgressHistoryEndpoint>
{
    public GetProgressHistorySummary()
    {
        Summary = "Get answer history";
        Description =
            "Returns draft and submitted entries in the caller's scope, newest period first, with each period's local dates and UTC " +
            "boundaries, status, actual submission time, and lateness. Answers are interpreted through the entry's own published " +
            "version, so later publications never change their labels, options, or scales. Attachments return media IDs and " +
            "metadata only; request content through GetMediaDownloadUrl, which authorizes each request separately. " +
            "Filter by plan or patient, by period start dates (inclusive), and by status. Pages hold 1 to 100 entries. " +
            "Doctors must specify a plan or patient, and see only plans they own or are granted, for patients they treat.";
    }
}
