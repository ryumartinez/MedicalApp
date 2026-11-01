namespace MedicalApp.Common.Domain.ApplicationIdentities;

public static class ApplicationIdentityErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "ApplicationIdentity.NotFound",
        "Account is not found");

    public static readonly Error AccountInUse = Error.Conflict(
        "ApplicationIdentity.AccountInUse",
        "The username or email belongs to an account that API provisioning did not create");

    public static readonly Error EmailInUse = Error.Conflict(
        "ApplicationIdentity.EmailInUse",
        "The email address belongs to another account");

    public static readonly Error MustBeDisabled = Error.Conflict(
        "ApplicationIdentity.MustBeDisabled",
        "Disable the account before changing its links");

    public static readonly Error AlreadyLinked = Error.Conflict(
        "ApplicationIdentity.AlreadyLinked",
        "The account or the record is already linked to another record or account");

    public static readonly Error ProviderAccountMissing = Error.Conflict(
        "ApplicationIdentity.ProviderAccountMissing",
        "The identity provider account no longer exists");

    public static Error Incomplete(IEnumerable<string> pendingSteps) => Error.Conflict(
        "ApplicationIdentity.Incomplete",
        $"Provisioning is incomplete: {string.Join(", ", pendingSteps)}");
}
