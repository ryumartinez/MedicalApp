namespace MedicalApp.Features.ProgressForms.GetProgressForm;

public class GetProgressFormSummary : Summary<GetProgressFormEndpoint>
{
    public GetProgressFormSummary()
    {
        Summary = "Get a form with its draft and published versions";
        Description =
            "Returns the caller's form: the editable draft with its revision, and every published version with its immutable questions. " +
            "Versions are ordered by version number. Returns 404 for missing forms and for forms owned by other doctors.";
    }
}
