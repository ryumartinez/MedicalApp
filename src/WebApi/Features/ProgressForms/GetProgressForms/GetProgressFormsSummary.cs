namespace MedicalApp.Features.ProgressForms.GetProgressForms;

public class GetProgressFormsSummary : Summary<GetProgressFormsEndpoint>
{
    public GetProgressFormsSummary()
    {
        Summary = "List the caller's forms";
        Description =
            "Lists only forms owned by the calling doctor, ordered by title. " +
            "Search matches part of the title without regard to case. Pages hold 1 to 100 forms.";
    }
}
