using System.Security.Cryptography;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Common.Storage;

public enum PromotionFailure
{
    Missing,
    TooLarge,
    Invalid
}

public sealed record PromotedObject(string FinalKey, long Size, string Sha256);

/// <summary>
/// Copies a client-writable staging object to a final key that clients can never write, with exactly the bytes that passed validation.
/// </summary>
/// <remarks>
/// Garage v2.4.1 cannot guarantee content identity on the server: conditional copy compares MD5 ETags, which can collide,
/// and conditional create-only writes are not supported. The promoter therefore takes a private snapshot. It reads the staging
/// object once into a temporary file, validates that file, and uploads the same file to the final key. A concurrent replacement
/// of the staging object cannot change what was validated or what is stored.
/// </remarks>
public sealed class ExactContentPromoter(IObjectStorage storage)
{
    public delegate Task<bool> Validator(Stream content, long size, CancellationToken cancellationToken);

    public async Task<ErrorOr<PromotedObject>> PromoteAsync(
        string stagingKey,
        string finalKey,
        string contentType,
        long maxBytes,
        Validator validate,
        CancellationToken cancellationToken)
    {
        var path = Path.Combine(Path.GetTempPath(), $"medicalapp-promotion-{Guid.NewGuid():N}");
        try
        {
            await using var snapshot = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920,
                FileOptions.DeleteOnClose | FileOptions.Asynchronous);

            long? size;
            try
            {
                size = await storage.DownloadAsync(stagingKey, snapshot, maxBytes, cancellationToken);
            }
            catch (ObjectTooLargeException)
            {
                return Error.Validation(nameof(PromotionFailure.TooLarge), "The uploaded file is too large.");
            }

            if (size is null)
                return Error.NotFound(nameof(PromotionFailure.Missing), "The uploaded file is missing.");

            snapshot.Position = 0;
            if (!await validate(snapshot, size.Value, cancellationToken))
                return Error.Validation(nameof(PromotionFailure.Invalid), "The uploaded file has an unsupported format.");

            snapshot.Position = 0;
            var sha256 = Convert.ToHexStringLower(await SHA256.HashDataAsync(snapshot, cancellationToken));

            await storage.PutAsync(finalKey, snapshot, contentType, cancellationToken);
            return new PromotedObject(finalKey, size.Value, sha256);
        }
        finally
        {
            // DeleteOnClose removes the snapshot. This covers a failure before the stream opened.
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}
