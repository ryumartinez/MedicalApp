using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Interfaces;
using Microsoft.Extensions.Options;

namespace MedicalApp.Common.Storage;

public sealed class MediaCleanupOptions
{
    public const string SectionName = "Media:Cleanup";

    public bool Enabled { get; set; } = true;
    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);
    public int BatchSize { get; set; } = 100;
}

public sealed record MediaCleanupResult(int Expired, int ObjectsDeleted, int StagingDeleted, int Failures);

/// <summary>
/// Expires abandoned uploads and unattached ready files, then removes their objects.
/// </summary>
/// <remarks>
/// Step 1 claims candidates with <c>FOR UPDATE SKIP LOCKED</c> and commits the Expired state. A submission holding an asset's
/// lock is skipped, and a committed attachment makes the asset ineligible. A committed expiry makes submission reject the asset.
/// Step 2 deletes objects outside any transaction and records success per asset, so failures are retried on the next run.
/// Attached assets are never expired or deleted.
/// </remarks>
public sealed class MediaCleanup(ApplicationDbContext dbContext, IObjectStorage storage, TimeProvider timeProvider,
    IOptions<MediaCleanupOptions> options, ILogger<MediaCleanup> logger)
{
    public async Task<MediaCleanupResult> RunOnceAsync(CancellationToken cancellationToken)
    {
        var batch = options.Value.BatchSize;
        var expired = await ExpireAsync(batch, cancellationToken);
        var (deleted, deleteFailures) = await DeleteExpiredObjectsAsync(batch, cancellationToken);
        var (staging, stagingFailures) = await DeletePromotedStagingAsync(batch, cancellationToken);
        return new MediaCleanupResult(expired, deleted, staging, deleteFailures + stagingFailures);
    }

    private async Task<int> ExpireAsync(int batch, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();
        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var claimed = await dbContext.ClaimExpirableAssetsAsync(
                pendingDeadline: now - MediaAsset.StagingRetention,
                readyDeadline: now - MediaAsset.UnattachedRetention, batch, cancellationToken);
            foreach (var asset in claimed.Where(asset => asset.IsExpiredBy(now)))
                asset.Expire(now);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return claimed.Count(asset => asset.State == MediaAssetState.Expired);
        });
    }

    private async Task<(int Deleted, int Failures)> DeleteExpiredObjectsAsync(int batch, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var assets = await dbContext.MediaAssets
            .Where(asset => asset.State == MediaAssetState.Expired && asset.ObjectsDeletedAt == null)
            .OrderBy(asset => asset.Id).Take(batch).ToListAsync(cancellationToken);
        var deleted = 0;
        var failures = 0;
        foreach (var asset in assets)
        {
            try
            {
                foreach (var key in asset.ObjectKeys)
                    await storage.DeleteAsync(key, cancellationToken);
                asset.MarkObjectsDeleted(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                deleted++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                logger.LogWarning(exception, "Could not delete objects of expired media asset {AssetId}; the next run retries.", asset.Id.Value);
                dbContext.Entry(asset).State = EntityState.Detached;
            }
        }

        return (deleted, failures);
    }

    private async Task<(int Deleted, int Failures)> DeletePromotedStagingAsync(int batch, CancellationToken cancellationToken)
    {
        dbContext.ChangeTracker.Clear();
        var assets = await dbContext.MediaAssets
            .Where(asset => (asset.State == MediaAssetState.Ready || asset.State == MediaAssetState.Attached) && asset.StagingDeletedAt == null)
            .OrderBy(asset => asset.Id).Take(batch).ToListAsync(cancellationToken);
        var deleted = 0;
        var failures = 0;
        foreach (var asset in assets)
        {
            try
            {
                // Only the staging copy. The final object of a ready or attached asset is never deleted here.
                await storage.DeleteAsync(asset.StagingKey, cancellationToken);
                asset.MarkStagingDeleted(timeProvider.GetUtcNow());
                await dbContext.SaveChangesAsync(cancellationToken);
                deleted++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures++;
                logger.LogWarning(exception, "Could not delete staging object of media asset {AssetId}; the next run retries.", asset.Id.Value);
                dbContext.Entry(asset).State = EntityState.Detached;
            }
        }

        return (deleted, failures);
    }
}

/// <summary>
/// Runs <see cref="MediaCleanup"/> on a fixed interval without patient requests.
/// </summary>
public sealed class MediaCleanupService(IServiceScopeFactory scopes, IOptions<MediaCleanupOptions> options, ILogger<MediaCleanupService> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;

        using var timer = new PeriodicTimer(options.Value.Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                var result = await scope.ServiceProvider.GetRequiredService<MediaCleanup>().RunOnceAsync(stoppingToken);
                if (result != new MediaCleanupResult(0, 0, 0, 0))
                    logger.LogInformation("Media cleanup: {Result}", result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogError(exception, "Media cleanup failed; the next run retries.");
            }
        }
    }
}
