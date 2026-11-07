namespace MedicalApp.Features.Accounts.SetAccountRoles;

public class SetAccountRolesSummary : Summary<SetAccountRolesEndpoint>
{
    public SetAccountRolesSummary()
    {
        Summary = "Set an account's API roles";
        Description =
            "Makes the account's medicalapp-api client roles exactly match the supplied list of doctor, patient, and administrator. " +
            "Repeating the request changes nothing. New roles appear in the next access token; issued tokens keep their roles for up to five minutes. " +
            "Doctor and patient roles grant access only together with an enabled, linked mapping. " +
            "Requires the administrator API role.";

        ExampleRequest = new SetAccountRolesRequest(Guid.Parse("01967a92-2ff8-7a00-8000-000000000010"), ["doctor"]);
    }
}
