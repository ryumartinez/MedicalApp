namespace MedicalApp.Features.ProgressForms.PublishProgressForm;

public class PublishProgressFormSummary : Summary<PublishProgressFormEndpoint>
{
    public PublishProgressFormSummary()
    {
        Summary = "Publish the draft as a new version";
        Description =
            "Validates the whole draft and stores it as an immutable version with the next version number and the publication time. " +
            "Questions keep their keys and option IDs and receive new version-specific IDs; answers always reference the version's IDs. " +
            "Validation requires at least one question, a label on every question and option, an integer scale with min below max " +
            "and a step that divides the range, 2 to 20 options for single choice, and allowed formats of the right kind for attachments. " +
            "Returns 400 with every problem and publishes nothing when the draft is invalid. " +
            "Returns 409 when the revision is stale or a concurrent publication took the number.";
    }
}
