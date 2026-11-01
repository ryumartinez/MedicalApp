namespace MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;

public class UpdateProgressFormDraftSummary : Summary<UpdateProgressFormDraftEndpoint>
{
    public UpdateProgressFormDraftSummary()
    {
        Summary = "Replace a form draft";
        Description =
            "Replaces the draft's title, description, and ordered questions. Send the revision you read; " +
            "if another change committed since then, the API returns 409 and keeps the committed draft. " +
            "Draft changes never alter published versions, the plans that use them, or historical answers. " +
            "Keep question keys and option IDs to preserve their identity in later versions. " +
            "Returns 404 for forms the caller does not own.";
    }
}
