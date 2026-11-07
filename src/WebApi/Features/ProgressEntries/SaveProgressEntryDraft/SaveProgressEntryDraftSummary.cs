namespace MedicalApp.Features.ProgressEntries.SaveProgressEntryDraft;

public class SaveProgressEntryDraftSummary : Summary<SaveProgressEntryDraftEndpoint>
{
    public SaveProgressEntryDraftSummary()
    {
        Summary = "Save draft answers for a period";
        Description =
            "Creates or replaces the draft for one period of the caller's open plan. The period must have started; past periods " +
            "are allowed while the plan is open. Each plan period has one entry: omit Revision to create it, and send the revision " +
            "you read to update it. A stale or missing revision returns 409 and keeps the committed answers. " +
            "Drafts may omit required answers, but every supplied answer must reference a question of the assigned version, " +
            "appear once, carry exactly one value of the right type, and respect the question's rules: text up to 4,000 characters " +
            "and not blank, whole numbers on the scale and step, an option ID of that version, or a ready media asset uploaded for " +
            "this entry and question. The acting patient and all timestamps come from the server. Submitted entries cannot change.";
    }
}
