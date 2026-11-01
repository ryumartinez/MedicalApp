using MedicalApp.Common.Domain.MediaAssets;

namespace MedicalApp.Features.MediaAssets;

/// <summary>
/// Media metadata. Content is only available through a separately authorized temporary download URL.
/// </summary>
public record MediaAssetDto(
    Guid Id,
    Guid EntryId,
    Guid QuestionId,
    MediaKind Kind,
    string ContentType,
    MediaAssetState State,
    long? Size,
    string? Sha256,
    DateTimeOffset UploadExpiresAt,
    DateTimeOffset? ReadyAt,
    DateTimeOffset? AttachedAt)
{
    public static MediaAssetDto From(MediaAsset asset) => new(
        asset.Id.Value, asset.EntryId.Value, asset.QuestionId.Value, asset.Kind, asset.ContentType, asset.State, asset.Size,
        asset.Sha256, asset.UploadExpiresAt, asset.ReadyAt, asset.AttachedAt);
}
