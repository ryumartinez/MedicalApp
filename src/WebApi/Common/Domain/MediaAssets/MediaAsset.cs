using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.Common.Domain.MediaAssets;

[ValueObject<Guid>]
public readonly partial struct MediaAssetId;

/// <summary>
/// One uploaded file for an image or video answer. It moves through pending, ready, attached, and expired.
/// Only submission attaches a ready asset.
/// </summary>
public class MediaAsset : Entity<MediaAssetId>
{
    public const long MaxImageBytes = 10L * 1024 * 1024;
    public const long MaxVideoBytes = 200L * 1024 * 1024;
    public static readonly TimeSpan UploadUrlLifetime = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DownloadUrlLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long an unfinalized upload stays available for finalization after its URL expires.</summary>
    public static readonly TimeSpan StagingRetention = TimeSpan.FromHours(24);

    /// <summary>How long a ready asset that no submitted entry attaches is kept.</summary>
    public static readonly TimeSpan UnattachedRetention = TimeSpan.FromDays(7);

    public const string Jpeg = "image/jpeg";
    public const string Png = "image/png";
    public const string WebP = "image/webp";
    public const string Mp4 = "video/mp4";

    public static readonly IReadOnlyList<string> ImageContentTypes = [Jpeg, Png, WebP];
    public static readonly IReadOnlyList<string> VideoContentTypes = [Mp4];

    public PatientId PatientId { get; private set; }
    public ProgressEntryId EntryId { get; private set; }
    public ProgressQuestionId QuestionId { get; private set; }
    public MediaKind Kind { get; private set; }
    public string ContentType { get; private set; } = null!;
    public MediaAssetState State { get; private set; }
    public string StagingKey { get; private set; } = null!;

    /// <summary>Final key of a promotion that started but has not completed. Cleanup and retries remove its object.</summary>
    public string? PromotionKey { get; private set; }

    public string? FinalKey { get; private set; }
    public long? Size { get; private set; }
    public string? Sha256 { get; private set; }
    public DateTimeOffset UploadExpiresAt { get; private set; }
    public DateTimeOffset? ReadyAt { get; private set; }
    public DateTimeOffset? AttachedAt { get; private set; }
    public DateTimeOffset? ExpiredAt { get; private set; }

    /// <summary>Set once cleanup has removed every stored object of an expired asset.</summary>
    public DateTimeOffset? ObjectsDeletedAt { get; private set; }

    /// <summary>Set once the staging object of a promoted asset is removed.</summary>
    public DateTimeOffset? StagingDeletedAt { get; private set; }

    public uint Revision { get; private set; }

    private MediaAsset() { }

    public static MediaAsset Create(PatientId patientId, ProgressEntryId entryId, ProgressQuestionId questionId, string contentType,
        DateTimeOffset now)
    {
        var kind = KindOf(contentType) ?? throw new ArgumentException("Unsupported content type.", nameof(contentType));
        var id = MediaAssetId.From(Guid.CreateVersion7());
        return new MediaAsset
        {
            Id = id,
            PatientId = patientId,
            EntryId = entryId,
            QuestionId = questionId,
            Kind = kind,
            ContentType = contentType,
            State = MediaAssetState.Pending,
            // Server-chosen keys. Clients only ever receive signed URLs for the staging key.
            StagingKey = $"staging/{id.Value:N}",
            UploadExpiresAt = now.Add(UploadUrlLifetime)
        };
    }

    public long MaxBytes => Kind == MediaKind.Image ? MaxImageBytes : MaxVideoBytes;

    public DateTimeOffset FinalizationDeadline => UploadExpiresAt.Add(StagingRetention);

    public bool CanFinalize(DateTimeOffset now) => State == MediaAssetState.Pending && now < FinalizationDeadline;

    public void BeginPromotion(string finalKey)
    {
        if (State != MediaAssetState.Pending)
            throw new InvalidOperationException("Only a pending asset can be promoted.");
        PromotionKey = finalKey;
    }

    public void CancelPromotion() => PromotionKey = null;

    public void MarkReady(string finalKey, long size, string sha256, DateTimeOffset now)
    {
        if (State != MediaAssetState.Pending || PromotionKey != finalKey)
            throw new InvalidOperationException("The asset is not promoting this key.");
        ThrowIfGreaterThan(size, MaxBytes, nameof(size));
        FinalKey = finalKey;
        PromotionKey = null;
        Size = size;
        Sha256 = sha256;
        ReadyAt = now;
        State = MediaAssetState.Ready;
    }

    /// <summary>
    /// Called only by the submission transaction.
    /// </summary>
    public void Attach(DateTimeOffset now)
    {
        if (State != MediaAssetState.Ready)
            throw new InvalidOperationException("Only a ready asset can be attached.");
        State = MediaAssetState.Attached;
        AttachedAt = now;
    }

    public bool IsExpiredBy(DateTimeOffset now) => State switch
    {
        MediaAssetState.Pending => now >= FinalizationDeadline,
        MediaAssetState.Ready => now >= ReadyAt!.Value.Add(UnattachedRetention),
        _ => false
    };

    /// <summary>
    /// Called by cleanup. Attached assets never expire.
    /// </summary>
    public void Expire(DateTimeOffset now)
    {
        if (State is MediaAssetState.Attached or MediaAssetState.Expired)
            throw new InvalidOperationException("Attached or expired assets cannot expire.");
        State = MediaAssetState.Expired;
        ExpiredAt = now;
    }

    public IReadOnlyList<string> ObjectKeys => new[] { StagingKey, PromotionKey, FinalKey }.OfType<string>().ToList();

    public void MarkObjectsDeleted(DateTimeOffset now)
    {
        if (State != MediaAssetState.Expired)
            throw new InvalidOperationException("Only expired assets lose their objects.");
        ObjectsDeletedAt = now;
        StagingDeletedAt ??= now;
    }

    public void MarkStagingDeleted(DateTimeOffset now) => StagingDeletedAt ??= now;

    public static bool IsSupportedContentType(string? contentType) => KindOf(contentType) is not null;

    public static MediaKind? KindOf(string? contentType) => contentType switch
    {
        Jpeg or Png or WebP => MediaKind.Image,
        Mp4 => MediaKind.Video,
        _ => null
    };

    /// <summary>
    /// Detects the actual format from the file signature. Returns null for unsupported content.
    /// </summary>
    public static string? DetectContentType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            return Jpeg;
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
            return Png;
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8))
            return WebP;
        if (header.Length >= 12 && header[4..8].SequenceEqual("ftyp"u8))
            return Mp4;
        return null;
    }
}
