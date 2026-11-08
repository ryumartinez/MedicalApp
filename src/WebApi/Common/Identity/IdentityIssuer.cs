using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Resolves the token issuer. The API, the authorization server, and the migration service use the same normalized
/// form, so the <c>iss</c> claim matches stored mappings exactly.
/// </summary>
public static class IdentityIssuer
{
    public const string ConfigurationKey = "Authentication:Issuer";

    /// <summary>
    /// The local API address from <c>launchSettings.json</c>, used in Development when no issuer is configured.
    /// </summary>
    public const string DevelopmentDefault = "https://localhost:7255/";

    /// <summary>
    /// Production requires an explicit HTTPS issuer. Development accepts any absolute URL and defaults to the local API address.
    /// </summary>
    public static Uri Resolve(IConfiguration configuration, IHostEnvironment environment)
    {
        var configured = configuration[ConfigurationKey];
        if (environment.IsDevelopment())
            return Normalize(string.IsNullOrWhiteSpace(configured) ? DevelopmentDefault : configured, requireHttps: false);

        return Normalize(configured, requireHttps: true);
    }

    /// <summary>
    /// Validates the URL and returns it with exactly one trailing slash.
    /// </summary>
    public static Uri Normalize(string? issuer, bool requireHttps = true)
    {
        if (!Uri.TryCreate(issuer?.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && (requireHttps || uri.Scheme != Uri.UriSchemeHttp))
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException(
                $"{ConfigurationKey} must be an absolute {(requireHttps ? "HTTPS " : "")}URL without query, fragment, or user information.");

        return new Uri(uri.GetLeftPart(UriPartial.Path).TrimEnd('/') + "/");
    }
}
