namespace MedicalApp.Common.Interfaces;

/// <summary>
/// An identity provider account as seen by trusted provisioning.
/// </summary>
/// <param name="ProvisioningId">The provisioning marker. Null when the account was not created through API provisioning.</param>
public sealed record IdentityProviderAccount(
    string Subject,
    string Username,
    bool Enabled,
    string? ProvisioningId,
    IReadOnlySet<string> ApiRoles);

public sealed record NewIdentityProviderAccount(string Username, string Email, string FirstName, string LastName);

/// <summary>
/// Trusted account operations in the identity provider. Implementations keep their management credentials private.
/// </summary>
public interface IIdentityProviderAccounts
{
    Task<IdentityProviderAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken);

    Task<IdentityProviderAccount?> FindBySubjectAsync(string subject, CancellationToken cancellationToken);

    /// <summary>
    /// Creates an account marked with the provisioning ID. Returns null when the username or email is already in use.
    /// </summary>
    Task<IdentityProviderAccount?> CreateAsync(NewIdentityProviderAccount account, string provisioningId, CancellationToken cancellationToken);

    /// <summary>
    /// Makes the account's API client roles exactly match <paramref name="roles"/>.
    /// </summary>
    Task SetApiRolesAsync(string subject, IReadOnlySet<string> roles, CancellationToken cancellationToken);

    Task SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken);

    /// <summary>
    /// Ends the account's sessions so refresh tokens cannot issue new access tokens.
    /// </summary>
    Task LogoutAsync(string subject, CancellationToken cancellationToken);
}
