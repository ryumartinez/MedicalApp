namespace MedicalApp.Features.Accounts.CreateAccount;

public class CreateAccountSummary : Summary<CreateAccountEndpoint>
{
    public CreateAccountSummary()
    {
        Summary = "Create an account";
        Description =
            "Creates a sign-in account without a password and a disabled application mapping, in one transaction. Requires the administrator API role. " +
            "The mapping grants no progress access until the account is linked, given roles, and enabled. " +
            "Retrying with the same username returns the existing account and mapping without creating duplicates. " +
            "Returns 409 when the username or email belongs to an account that provisioning did not create. " +
            "The API never collects passwords: create a password setup link and give it to the account holder.";

        ExampleRequest = new CreateAccountRequest("jane.doe", "jane.doe@example.com", "Jane", "Doe");
        Response(200, "The account and its mapping.", example: new CreateAccountResponse(
            Guid.Parse("01967a92-2ff8-7a00-8000-000000000010"), "8c1d6f0e-1c64-4f7e-9b0a-2f1c9e8d7a65", "jane.doe"));
        Response(409, "The username or email is in use by an account that provisioning did not create.");
    }
}
