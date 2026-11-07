using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Storage;

namespace MedicalApp.Features.MediaAssets.FinalizeMediaUpload;

public class FinalizeMediaUploadEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    IObjectStorage storage,
    ExactContentPromoter promoter,
    TimeProvider timeProvider,
    ILogger<FinalizeMediaUploadEndpoint> logger) : EndpointWithoutRequest<MediaAssetDto>
{
    public override void Configure()
    {
        Post("/{id:guid}/finalize");
        Group<MediaAssetsGroup>();
        Policies(ProgressPolicies.Patient);
        Description(x => x.WithName("FinalizeMediaUpload")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var patientId = await identities.GetPatientIdAsync(ct);
        var assetId = MediaAssetId.From(Route<Guid>("id"));
        var asset = await dbContext.MediaAssets.SingleOrDefaultAsync(a => a.Id == assetId && a.PatientId == patientId, ct);
        if (asset is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var result = await FinalizeAsync(asset, ct);
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        await Send.OkAsync(MediaAssetDto.From(result.Value), ct);
    }

    private async Task<ErrorOr<MediaAsset>> FinalizeAsync(MediaAsset asset, CancellationToken ct)
    {
        // A repeated finalization returns the existing result.
        if (asset.State is MediaAssetState.Ready or MediaAssetState.Attached)
            return asset;
        if (!asset.CanFinalize(timeProvider.GetUtcNow()))
            return MediaAssetErrors.Expired;

        // Remove the object of an earlier attempt that stopped before the asset became ready.
        if (asset.PromotionKey is { } abandoned)
            await storage.DeleteAsync(abandoned, ct);

        // Record the final key before writing it, so cleanup can find it if this attempt stops halfway.
        // The revision check lets only one concurrent attempt continue.
        var finalKey = $"final/{asset.Id.Value:N}/{Guid.NewGuid():N}";
        asset.BeginPromotion(finalKey);
        if (!await TrySaveAsync(ct))
            return MediaAssetErrors.Conflict;

        var promoted = await promoter.PromoteAsync(asset.StagingKey, finalKey, asset.ContentType, asset.MaxBytes,
            (content, size, token) => IsValidAsync(asset, content, size, token), ct);
        if (promoted.IsError)
        {
            asset.CancelPromotion();
            await TrySaveAsync(ct);
            return promoted.FirstError.Code switch
            {
                nameof(PromotionFailure.Missing) => MediaAssetErrors.Missing,
                nameof(PromotionFailure.TooLarge) => MediaAssetErrors.TooLarge,
                _ => MediaAssetErrors.InvalidFormat
            };
        }

        asset.MarkReady(finalKey, promoted.Value.Size, promoted.Value.Sha256, timeProvider.GetUtcNow());
        if (!await TrySaveAsync(ct))
        {
            // Cleanup or another request changed the asset. This attempt's object is not referenced.
            await storage.DeleteAsync(finalKey, ct);
            return MediaAssetErrors.Conflict;
        }

        try
        {
            await storage.DeleteAsync(asset.StagingKey, ct);
            asset.MarkStagingDeleted(timeProvider.GetUtcNow());
            await TrySaveAsync(ct);
        }
        catch (Exception exception)
        {
            // Cleanup removes the staging object later.
            logger.LogWarning(exception, "Could not delete staging object for media asset {AssetId}.", asset.Id.Value);
        }

        return asset;
    }

    private static async Task<bool> IsValidAsync(MediaAsset asset, Stream content, long size, CancellationToken ct)
    {
        if (size == 0)
            return false;
        var header = new byte[16];
        var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, ct);
        return MediaAsset.DetectContentType(header.AsSpan(0, read)) == asset.ContentType;
    }

    private async Task<bool> TrySaveAsync(CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            return false;
        }
    }
}
