namespace MedicalApp.Common.Interfaces;

public sealed record StoredObjectInfo(string Key, long Size, string? ContentType, string ETag);

public sealed class ObjectTooLargeException(string key, long maxBytes)
    : Exception($"Object '{key}' exceeds {maxBytes} bytes.");

/// <summary>
/// Private object storage. Browsers reach objects only through short-lived signed URLs for a single key.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Creates a signed URL that lets a browser PUT one object at <paramref name="key"/> with exactly this content type.
    /// Signed URLs cannot limit size, so callers validate the stored object afterwards.
    /// </summary>
    Task<Uri> CreateUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Creates a signed URL that lets a browser GET one object.
    /// </summary>
    Task<Uri> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken);

    /// <summary>
    /// Returns null when the object does not exist.
    /// </summary>
    Task<StoredObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken);

    /// <summary>
    /// Copies the object's current bytes to <paramref name="destination"/>. Returns null when the object does not exist.
    /// Throws <see cref="ObjectTooLargeException"/> as soon as more than <paramref name="maxBytes"/> arrive.
    /// </summary>
    Task<long?> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken);

    /// <summary>
    /// Stores the seekable stream's bytes at the key. The storage service verifies their MD5 digest in transit.
    /// </summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>
    /// Deletes the object. Deleting a missing object succeeds.
    /// </summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
