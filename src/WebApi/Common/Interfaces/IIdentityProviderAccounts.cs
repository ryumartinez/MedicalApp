namespace MedicalApp.Common.Interfaces;

/// <summary>
/// A sign-in account as seen by trusted provisioning.
/// </summary>
public sealed record IdentityProviderAccount(
    string Subject,
    string Username,
    bool Enabled,
    bool HasPassword,
    IReadOnlySet<string> ApiRoles);

public sealed record NewIdentityProviderAccount(string Username, string Email, string FirstName, string LastName);

/// <summary>
/// Trusted account operations on the authorization server's accounts.
/// Calls share the request's database context, so a caller's transaction covers them.
/// </summary>
public interface IIdentityProviderAccounts
{
    Task<IdentityProviderAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<IdentityProviderAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<IdentityProviderAccount?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an enabled account without a password. Returns null when the username or email is already in use.
    /// </summary>
    Task<IdentityProviderAccount?> CreateAsync(NewIdentityProviderAccount account, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the account's API roles exactly match <paramref name="roles"/>.
    /// </summary>
    Task SetApiRolesAsync(string subject, IReadOnlySet<string> roles, CancellationToken cancellationToken);

    Task SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Ends the account's sign-in sessions and revokes its authorization codes, tokens, and authorizations.
    /// </summary>
    Task LogoutAsync(string subject, CancellationToken cancellationToken);
}
