using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MedicalApp.Common.Identity;

/// <summary>
/// Chooses the authorization server's signing and encryption keys.
/// Development uses persisted development certificates unless certificates or ephemeral keys are configured.
/// Every other environment requires configured certificates.
/// </summary>
public static class ServerCredentials
{
    public const string SigningKey = "Authentication:Certificates:Signing";
    public const string EncryptionKey = "Authentication:Certificates:Encryption";

    /// <summary>
    /// Development only. Tests use throwaway keys so they never touch the user's certificate store.
    /// </summary>
    public const string EphemeralKeysKey = "Authentication:Certificates:UseEphemeralKeys";

    public static OpenIddictServerBuilder AddServerCredentials(this OpenIddictServerBuilder server, IConfiguration configuration,
        IHostEnvironment environment)
    {
        if (environment.IsDevelopment())
        {
            if (configuration.GetValue<bool>(EphemeralKeysKey))
                return server.AddEphemeralSigningKey().AddEphemeralEncryptionKey();

            if (!configuration.GetSection(SigningKey).Exists() && !configuration.GetSection(EncryptionKey).Exists())
                return server.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
        }

        // The first certificate signs new tokens. Later ones stay valid for tokens signed before a rotation.
        foreach (var certificate in LoadCertificates(configuration, SigningKey))
            server.AddSigningCertificate(certificate);
        foreach (var certificate in LoadCertificates(configuration, EncryptionKey))
            server.AddEncryptionCertificate(certificate);

        return server;
    }

    /// <summary>
    /// Reads one certificate (<c>Path</c> or <c>Base64</c>, with an optional <c>Password</c>) or a list of them.
    /// </summary>
    public static IReadOnlyList<X509Certificate2> LoadCertificates(IConfiguration configuration, string key)
    {
        var section = configuration.GetSection(key);
        var entries = section["Path"] is not null || section["Base64"] is not null
            ? [section]
            : section.GetChildren().ToList();

        var certificates = entries.Select(entry => Load(entry, key)).ToList();
        if (certificates.Count == 0)
            throw new InvalidOperationException($"{key} is required. Configure a PFX certificate with a Path or Base64 value.");

        return certificates;
    }

    private static X509Certificate2 Load(IConfigurationSection entry, string key)
    {
        var password = entry["Password"];
        // macOS cannot load ephemeral key sets. Elsewhere keep private keys out of the machine key store.
        var flags = OperatingSystem.IsMacOS() ? X509KeyStorageFlags.DefaultKeySet : X509KeyStorageFlags.EphemeralKeySet;

        var certificate = entry["Path"] is { Length: > 0 } path
            ? X509CertificateLoader.LoadPkcs12FromFile(path, password, flags)
            : entry["Base64"] is { Length: > 0 } base64
                ? X509CertificateLoader.LoadPkcs12(Convert.FromBase64String(base64), password, flags)
                : throw new InvalidOperationException($"{entry.Path} needs a Path or Base64 value.");

        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException($"The certificate in {key} has no private key.");

        return certificate;
    }
}
