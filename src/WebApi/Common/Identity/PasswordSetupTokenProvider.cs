using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace MedicalApp.Common.Identity;

public sealed class PasswordSetupTokenProviderOptions : DataProtectionTokenProviderOptions
{
    public const string ProviderName = "PasswordSetup";
    public const string LifetimeKey = "Authentication:PasswordSetup:Lifetime";

    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(24);

    public PasswordSetupTokenProviderOptions()
    {
        Name = ProviderName;
        TokenLifespan = DefaultLifetime;
    }
}

/// <summary>
/// Issues password setup tokens. A token is bound to the account's security stamp, so it stops working once a
/// password is set or a newer link is issued.
/// </summary>
public sealed class PasswordSetupTokenProvider(
    IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordSetupTokenProviderOptions> options,
    ILogger<DataProtectorTokenProvider<ApplicationUser>> logger)
    : DataProtectorTokenProvider<ApplicationUser>(dataProtectionProvider, options, logger);
