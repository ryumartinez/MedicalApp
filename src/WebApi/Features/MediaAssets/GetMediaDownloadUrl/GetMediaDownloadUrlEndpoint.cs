using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;

public class GetMediaDownloadUrlEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    ProgressAuthorizationService authorization,
    IObjectStorage storage,
    TimeProvider timeProvider) : EndpointWithoutRequest<GetMediaDownloadUrlResponse>
{
    public override void Configure()
    {
        Get("/{id:guid}/download-url");
        Group<MediaAssetsGroup>();
        Description(x => x.WithName("GetMediaDownloadUrl").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var assetId = MediaAssetId.From(Route<Guid>("id"));
        var visiblePlans = authorization.PlansVisibleTo(identity).Select(plan => plan.Id);
        var visibleEntries = dbContext.ProgressEntries.Where(entry => visiblePlans.Contains(entry.PlanId)).Select(entry => entry.Id);
        var asset = await dbContext.MediaAssets.AsNoTracking()
            .Where(a => a.Id == assetId && visibleEntries.Contains(a.EntryId))
            .SingleOrDefaultAsync(ct);
        if (asset is not { State: MediaAssetState.Ready or MediaAssetState.Attached, FinalKey: { } finalKey })
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var url = await storage.CreateDownloadUrlAsync(finalKey, MediaAsset.DownloadUrlLifetime, ct);
        await Send.OkAsync(new GetMediaDownloadUrlResponse(asset.Id.Value, url, asset.ContentType, asset.Size!.Value,
            timeProvider.GetUtcNow().Add(MediaAsset.DownloadUrlLifetime)), ct);
    }
}
