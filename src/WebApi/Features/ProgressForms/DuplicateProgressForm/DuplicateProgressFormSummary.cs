namespace MedicalApp.Features.ProgressForms.DuplicateProgressForm;

public class DuplicateProgressFormSummary : Summary<DuplicateProgressFormEndpoint>
{
    public DuplicateProgressFormSummary()
    {
        Summary = "Duplicate a form";
        Description =
            "Copies the form's current draft into a new form owned by the caller, for example to customize it for one patient. " +
            "The copy starts without published versions and is edited and published independently. " +
            "The source form, its versions, and its plans never change.";
    }
}
