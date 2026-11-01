namespace MedicalApp.Features.ProgressEntries.SubmitProgressEntry;

public class SubmitProgressEntrySummary : Summary<SubmitProgressEntryEndpoint>
{
    public SubmitProgressEntrySummary()
    {
        Summary = "Submit the saved draft";
        Description =
            "Submits the entry's saved draft. An optional revision query parameter requires that the draft is unchanged. Every required question needs an answer, and every media answer needs a ready asset " +
            "uploaded by the caller for this entry and question. The server records the actual submission time; a submission " +
            "after the period ended is accepted while the plan is open and is marked late, keeping its original period. " +
            "Submission locks the plan, the entry, and the referenced media in that order, and commits the submitted entry and the " +
            "attached media in one transaction: if anything fails, neither changes. A plan closure that commits first makes " +
            "submission fail with 409. Submitted entries are immutable. Retrying a submission that already committed returns the " +
            "same entry with 200 and writes nothing, even when the revision is stale.";
    }
}
