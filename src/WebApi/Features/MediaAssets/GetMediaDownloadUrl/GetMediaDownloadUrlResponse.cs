namespace MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;

/// <param name="DownloadUrl">Signed URL for one object, valid for five minutes. Anyone holding it can read the file until it expires.</param>
public record GetMediaDownloadUrlResponse(Guid AssetId, Uri DownloadUrl, string ContentType, long Size, DateTimeOffset ExpiresAt);
