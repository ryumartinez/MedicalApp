namespace MedicalApp.Features.MediaAssets.RequestMediaUpload;

/// <param name="UploadUrl">Signed URL for one HTTP PUT of the file to a staging key.</param>
/// <param name="Headers">Headers the upload must send.</param>
public record RequestMediaUploadResponse(
    Guid AssetId,
    Uri UploadUrl,
    string Method,
    IReadOnlyDictionary<string, string> Headers,
    long MaxBytes,
    DateTimeOffset ExpiresAt);
