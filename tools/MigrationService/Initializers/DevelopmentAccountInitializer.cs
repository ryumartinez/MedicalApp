using MedicalApp.Common.Authentication;
using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MigrationService.Initializers;

/// <summary>
/// Seeds the development doctor, patient, and administrator accounts. Runs in Development only.
/// Existing accounts keep their roles and passwords. Accounts without a password get the shared development password.
/// </summary>
public class DevelopmentAccountInitializer(
    UserManager<ApplicationUser> users,
    IOptions<IdentityOptions> identityOptions,
    IConfiguration configuration,
    ILogger<DevelopmentAccountInitializer> logger)
{
    public const string PasswordKey = "Authentication:DevelopmentPassword";

    public static readonly Guid DoctorUserId = Guid.Parse(ProgressIdentityInitializer.DoctorSubject);
    public static readonly Guid PatientUserId = Guid.Parse(ProgressIdentityInitializer.PatientSubject);
    public static readonly Guid AdministratorUserId = Guid.Parse("01967a92-2ff8-7a00-8000-000000000003");

    public static readonly IReadOnlyList<DevelopmentAccount> Accounts =
    [
        new(DoctorUserId, "development-doctor", ApiRoles.Doctor),
        new(PatientUserId, "development-patient", ApiRoles.Patient),
        new(AdministratorUserId, "development-administrator", ApiRoles.Administrator)
    ];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var password = configuration[PasswordKey];
        if (string.IsNullOrEmpty(password))
            logger.LogWarning("Development accounts get no password because {PasswordKey} is not configured.", PasswordKey);
        else if (password.Length < identityOptions.Value.Password.RequiredLength)
            throw new InvalidOperationException(
                $"The development account password in {PasswordKey} must have at least {identityOptions.Value.Password.RequiredLength} characters.");

        foreach (var account in Accounts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var user = await EnsureAccountAsync(account);
            if (!string.IsNullOrEmpty(password) && !await users.HasPasswordAsync(user))
                Check(await users.AddPasswordAsync(user, password), account.Username);
        }
    }

    private async Task<ApplicationUser> EnsureAccountAsync(DevelopmentAccount account)
    {
        var byName = await users.FindByNameAsync(account.Username);
        if (byName is not null && byName.Id != account.Id)
            throw new InvalidOperationException(
                $"The development username {account.Username} belongs to account {byName.Id}, not {account.Id}. Rename or delete that account first.");

        var existing = byName ?? await users.FindByIdAsync(account.Id.ToString());
        if (existing is not null)
            return existing;

        var user = new ApplicationUser
        {
            Id = account.Id,
            UserName = account.Username,
            Email = $"{account.Username}@example.invalid",
            EmailConfirmed = true,
            Enabled = true
        };
        Check(await users.CreateAsync(user), account.Username);
        Check(await users.AddToRoleAsync(user, account.Role), account.Username);
        return user;
    }

    private static void Check(IdentityResult result, string username)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Could not initialize the development account {username}: {string.Join(" ", result.Errors.Select(error => error.Description))}");
    }

    public sealed record DevelopmentAccount(Guid Id, string Username, string Role);
}
