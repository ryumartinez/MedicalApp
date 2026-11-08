using Microsoft.AspNetCore.Authentication;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Limits of the authorization server's sign-in session. The cookie slides on activity; the total limit does not.
/// </summary>
public static class SignInSession
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan MaximumLifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// Stored in the cookie's properties, which survive sliding renewal and security stamp refreshes.
    /// </summary>
    public const string SignedInAtKey = ".medicalapp.signed_in_at";

    public static void MarkSignedIn(AuthenticationProperties properties, DateTimeOffset now)
    {
        if (!properties.Items.ContainsKey(SignedInAtKey))
            properties.Items[SignedInAtKey] = now.ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// True when the session started more than <see cref="MaximumLifetime"/> ago, or has no recorded start.
    /// </summary>
    public static bool HasExpired(AuthenticationProperties properties, DateTimeOffset now) =>
        !properties.Items.TryGetValue(SignedInAtKey, out var value)
        || !long.TryParse(value, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
        || now - DateTimeOffset.FromUnixTimeSeconds(seconds) > MaximumLifetime;
}
