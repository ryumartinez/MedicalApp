namespace MedicalApp.Features.Accounts.LinkAccount;

public class LinkAccountSummary : Summary<LinkAccountEndpoint>
{
    public LinkAccountSummary()
    {
        Summary = "Link an account to doctor or patient records";
        Description =
            "Links a disabled account to an existing doctor or patient by ID, or creates a new record and links it. " +
            "Email addresses and usernames never establish links. One account can link to one doctor and one patient. " +
            "Retries return the existing link and never create a second record. " +
            "Returns 409 when the account is enabled, already linked to a different record, or the record belongs to another account. " +
            "Requires the administrator API role.";

        ExampleRequest = new LinkAccountRequest(Guid.Parse("01967a92-2ff8-7a00-8000-000000000010"), null, null,
            new LinkAccountRequest.NewDoctorDto("Dr. Jane Doe", "Psychology"), null);
    }
}
