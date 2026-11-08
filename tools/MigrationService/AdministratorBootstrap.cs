using MedicalApp.Common.Authentication;
using MedicalApp.Common.Identity;
using MedicalApp.Features.Accounts.CreatePasswordSetupLink;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MigrationService.Initializers;

namespace MigrationService;

/// <summary>
/// Creates the first administrator account and prints its password setup link. Administrators need no application mapping.
/// The link goes only to the operator's console, never to a logger, so it does not reach logs or telemetry.
/// </summary>
public sealed class AdministratorBootstrap(
    UserManager<ApplicationUser> users,
    IdentityClientInitializer clients,
    IOptions<PasswordSetupTokenProviderOptions> tokenOptions,
    IConfiguration configuration,
    IHostEnvironment environment,
    TimeProvider timeProvider)
{
    public const string Command = "bootstrap-admin";

    public const string Usage =
        "Usage: dotnet run --project tools/MigrationService -- bootstrap-admin --username <username> --email <email> [--force]";

    public sealed record Arguments(string Username, string Email, bool Force);

    /// <summary>
    /// Returns null when the arguments are incomplete.
    /// </summary>
    public static Arguments? Parse(IReadOnlyList<string> args)
    {
        string? username = null, email = null;
        var force = false;
        for (var i = 0; i < args.Count; i++)
        {
            switch (args[i])
            {
                case "--username" when i + 1 < args.Count:
                    username = args[++i];
                    break;
                case "--email" when i + 1 < args.Count:
                    email = args[++i];
                    break;
                case "--force":
                    force = true;
                    break;
                default:
                    break;
            }
        }

        return string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) ? null : new Arguments(username, email, force);
    }

    public async Task<int> RunAsync(Arguments arguments, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        // Fails before any change when the issuer for the link is missing or invalid.
        var issuer = IdentityIssuer.Resolve(configuration, environment);

        // Roles and the API scope are needed for the account to be useful.
        await clients.InitializeAsync(cancellationToken);

        var administrators = await users.GetUsersInRoleAsync(ApiRoles.Administrator);
        if (!arguments.Force && administrators.Any(user => user.Enabled))
        {
            await error.WriteLineAsync("An enabled administrator already exists. Nothing was created. Use --force to add another administrator.");
            return 1;
        }

        if (await users.FindByNameAsync(arguments.Username) is not null || await users.FindByEmailAsync(arguments.Email) is not null)
        {
            await error.WriteLineAsync($"The username or email of {arguments.Username} is already in use. Nothing was created.");
            return 1;
        }

        var user = new ApplicationUser
        {
            Id = Guid.CreateVersion7(),
            UserName = arguments.Username,
            Email = arguments.Email,
            Enabled = true
        };
        var created = await users.CreateAsync(user);
        if (created.Succeeded)
            created = await users.AddToRoleAsync(user, ApiRoles.Administrator);
        if (!created.Succeeded)
        {
            await error.WriteLineAsync($"Could not create the administrator: {string.Join(" ", created.Errors.Select(e => e.Description))}");
            return 1;
        }

        var link = await CreatePasswordSetupLinkEndpoint.CreateLinkAsync(users, user, issuer, tokenOptions.Value.TokenLifespan, timeProvider);
        await output.WriteLineAsync($"Created administrator {user.UserName} ({user.Subject}).");
        await output.WriteLineAsync($"Give this password setup link to the administrator. It works once and expires at {link.ExpiresAt:u}:");
        await output.WriteLineAsync(link.Url.AbsoluteUri);
        return 0;
    }
}
