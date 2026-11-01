namespace MedicalApp.Features.ProgressEntries.GetProgressEntry;

public class GetProgressEntrySummary : Summary<GetProgressEntryEndpoint>
{
    public GetProgressEntrySummary()
    {
        Summary = "Get an entry";
        Description =
            "Returns a draft or submitted entry with its period, status, actual submission time, lateness, revision, and answers. " +
            "The caller needs access to the entry's plan: the patient, the owning doctor with treatment, or a granted doctor with " +
            "treatment. Every other caller receives 404.";
    }
}
