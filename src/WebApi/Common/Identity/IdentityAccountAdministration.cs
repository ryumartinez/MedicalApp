using MedicalApp.Common.Authentication;
using MedicalApp.Common.Interfaces;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Manages authorization server accounts through Identity and the OpenIddict stores in the application database.
/// </summary>
public sealed class IdentityAccountAdministration(
    UserManager<ApplicationUser> users,
    IOpenIddictAuthorizationManager authorizations,
    IOpenIddictTokenManager tokens) : IIdentityProviderAccounts
{
    public async Task<IdentityProviderAccount?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        await users.FindByNameAsync(username) is { } user ? await ToAccountAsync(user) : null;

    public async Task<IdentityProviderAccount?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        await users.FindByEmailAsync(email) is { } user ? await ToAccountAsync(user) : null;

    public async Task<IdentityProviderAccount?> FindBySubjectAsync(string subject, CancellationToken cancellationToken) =>
        await FindUserAsync(subject) is { } user ? await ToAccountAsync(user) : null;

    public async Task<IdentityProviderAccount?> CreateAsync(NewIdentityProviderAccount account, CancellationToken cancellationToken)
    {
        if (await users.FindByEmailAsync(account.Email) is not null)
            return null;

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = account.Username,
            Email = account.Email,
            Enabled = true
        };
        var result = await users.CreateAsync(user);
        if (result.Errors.Any(error => error.Code is nameof(IdentityErrorDescriber.DuplicateUserName) or nameof(IdentityErrorDescriber.DuplicateEmail)))
            return null;
        Check(result);

        return await ToAccountAsync(user);
    }

    public async Task SetApiRolesAsync(string subject, IReadOnlySet<string> roles, CancellationToken cancellationToken)
    {
        var unknown = roles.Where(role => !ApiRoles.All.Contains(role)).ToList();
        if (unknown.Count != 0)
            throw new ArgumentException($"Unknown API roles: {string.Join(", ", unknown)}.", nameof(roles));

        var user = await RequireUserAsync(subject);
        var current = (await users.GetRolesAsync(user)).Where(ApiRoles.All.Contains).ToHashSet(StringComparer.Ordinal);

        var remove = current.Except(roles).ToList();
        if (remove.Count != 0)
            Check(await users.RemoveFromRolesAsync(user, remove));

        var add = roles.Except(current).ToList();
        if (add.Count != 0)
            Check(await users.AddToRolesAsync(user, add));
    }

    public async Task SetEnabledAsync(string subject, bool enabled, CancellationToken cancellationToken)
    {
        var user = await RequireUserAsync(subject);
        if (user.Enabled == enabled)
            return;

        user.Enabled = enabled;
        Check(await users.UpdateAsync(user));
        if (!enabled)
            Check(await users.UpdateSecurityStampAsync(user));
    }

    public async Task LogoutAsync(string subject, CancellationToken cancellationToken)
    {
        // A new security stamp ends sign-in cookies at their next request.
        Check(await users.UpdateSecurityStampAsync(await RequireUserAsync(subject)));

        // Revoked authorization codes can no longer be exchanged for tokens.
        await tokens.RevokeBySubjectAsync(subject, cancellationToken);
        await authorizations.RevokeBySubjectAsync(subject, cancellationToken);
    }

    private async Task<IdentityProviderAccount> ToAccountAsync(ApplicationUser user)
    {
        var roles = (await users.GetRolesAsync(user)).Where(ApiRoles.All.Contains).ToHashSet(StringComparer.Ordinal);
        return new IdentityProviderAccount(user.Subject, user.UserName!, user.Enabled, await users.HasPasswordAsync(user), roles);
    }

    private Task<ApplicationUser?> FindUserAsync(string subject) =>
        Guid.TryParse(subject, out var id) ? users.FindByIdAsync(id.ToString()) : Task.FromResult<ApplicationUser?>(null);

    private async Task<ApplicationUser> RequireUserAsync(string subject) =>
        await FindUserAsync(subject) ?? throw new InvalidOperationException($"Account {subject} does not exist.");

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
