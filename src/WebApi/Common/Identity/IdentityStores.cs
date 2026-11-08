using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;

namespace MedicalApp.Common.Identity;

public static class IdentityStores
{
    /// <summary>
    /// Shared by every host that writes tokens, so the migration service can create links the API accepts.
    /// </summary>
    public const string DataProtectionApplicationName = "MedicalApp";

    /// <summary>
    /// Registers Identity accounts, OpenIddict stores, and persistent data protection keys.
    /// The API and the migration service both use this, so they apply the same account rules.
    /// Sign-in needs ASP.NET Core authentication, so the API adds the sign-in manager itself.
    /// </summary>
    public static IServiceCollection AddIdentityStores(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<ApplicationDbContext>();

        services.AddIdentityCore<ApplicationUser>(ConfigureIdentity)
            .AddRoles<IdentityRole<Guid>>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordSetupTokenProvider>(PasswordSetupTokenProviderOptions.ProviderName);

        services.Configure<PasswordSetupTokenProviderOptions>(options =>
        {
            if (TimeSpan.TryParse(configuration[PasswordSetupTokenProviderOptions.LifetimeKey], out var lifetime) && lifetime > TimeSpan.Zero)
                options.TokenLifespan = lifetime;
        });

        services.AddOpenIddict()
            .AddCore(options => options
                .UseEntityFrameworkCore()
                .UseDbContext<ApplicationDbContext>()
                .ReplaceDefaultEntities<Guid>());

        return services;
    }

    public static void ConfigureIdentity(IdentityOptions options)
    {
        // Length over composition rules. Twelve characters is the minimum for staff-managed medical accounts.
        options.Password.RequiredLength = 12;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireDigit = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireNonAlphanumeric = false;

        options.User.RequireUniqueEmail = true;

        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);

        options.Tokens.PasswordResetTokenProvider = PasswordSetupTokenProviderOptions.ProviderName;

        // The sign-in cookie uses the same claim types as the tokens it produces.
        options.ClaimsIdentity.UserIdClaimType = OpenIddictConstants.Claims.Subject;
        options.ClaimsIdentity.UserNameClaimType = OpenIddictConstants.Claims.Name;
        options.ClaimsIdentity.RoleClaimType = OpenIddictConstants.Claims.Role;
        options.ClaimsIdentity.EmailClaimType = OpenIddictConstants.Claims.Email;
    }
}
