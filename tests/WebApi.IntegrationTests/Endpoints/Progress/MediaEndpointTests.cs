using System.Net;
using System.Net.Http.Json;
using Amazon.S3.Model;
using GarageInitializer;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Persistence;
using MedicalApp.Common.Storage;
using MedicalApp.Features.MediaAssets;
using MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;
using MedicalApp.Features.ProgressEntries;
using MedicalApp.Features.ProgressPlans;
using MedicalApp.Features.ProgressPlans.GetProgressPlan;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

[Collection<ProgressApiCollection>]
public sealed class MediaEndpointTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;
    private PlanDto _plan = null!;
    private GetProgressPlanResponse.FormVersionDto _version = null!;
    private EntryDto _draft = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await ProgressScenario.CreateAsync(fixture);
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        _plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-1));
        _version = await _s.PatientAClient.GetVersionAsync(_plan.Id);
        using var saved = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, RequiredAnswers(_version));
        _draft = await saved.ReadAsync<EntryDto>();
    }

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    private Guid Photo => _version.Q("photo");

    [Fact]
    public async Task UploadsFinalizesAndDownloadsPrivately()
    {
        var bytes = Png(4096);
        var upload = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);
        upload.UploadUrl.AbsolutePath.Should().Contain("/staging/");
        upload.Headers["Content-Type"].Should().Be(MediaAsset.Png);
        upload.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
        (await UploadAsync(upload.UploadUrl, MediaAsset.Png, bytes)).Status.Should().Be(HttpStatusCode.OK);

        using var pendingDownload = await _s.PatientAClient.GetAsync($"/api/media-assets/{upload.AssetId}/download-url", Ct);
        pendingDownload.StatusCode.Should().Be(HttpStatusCode.NotFound, "pending media has no finalized file");

        var ready = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();
        var again = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();
        ready.State.Should().Be(MediaAssetState.Ready);
        ready.Size.Should().Be(bytes.Length);
        ready.Sha256.Should().Be(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        again.Should().BeEquivalentTo(ready);

        // The staging URL can no longer change the finalized file.
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, Png(128));

        using var http = new HttpClient();
        var patientUrl = await _s.PatientAClient.GetFromJsonAsync<GetMediaDownloadUrlResponse>($"/api/media-assets/{upload.AssetId}/download-url", Ct);
        (await http.GetByteArrayAsync(patientUrl!.DownloadUrl, Ct)).Should().Equal(bytes);
        patientUrl.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromMinutes(1));
        var doctorUrl = await _s.DoctorAClient.GetFromJsonAsync<GetMediaDownloadUrlResponse>($"/api/media-assets/{upload.AssetId}/download-url", Ct);
        (await http.GetByteArrayAsync(doctorUrl!.DownloadUrl, Ct)).Should().Equal(bytes);

        using var otherPatient = await _s.PatientBClient.GetAsync($"/api/media-assets/{upload.AssetId}/download-url", Ct);
        using var otherDoctor = await _s.DoctorBClient.GetAsync($"/api/media-assets/{upload.AssetId}/download-url", Ct);
        using var otherFinalize = await _s.PatientBClient.FinalizeAsync(upload.AssetId);
        using var anonymous = await http.GetAsync(new Uri(fixture.Garage.S3Endpoint, $"{GarageContainer.Bucket}/{patientUrl.DownloadUrl.AbsolutePath.Split(GarageContainer.Bucket + "/")[1]}"), Ct);
        otherPatient.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherDoctor.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherFinalize.StatusCode.Should().Be(HttpStatusCode.NotFound);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task RejectsInvalidUploadRequests()
    {
        async Task<HttpStatusCode> Request(HttpClient client, Guid entry, Guid question, string type)
        {
            using var response = await client.PostAsJsonAsync("/api/media-assets/uploads", new { EntryId = entry, QuestionId = question, ContentType = type }, Ct);
            return response.StatusCode;
        }

        (await Request(_s.PatientAClient, _draft.Id, _version.Q("notes"), MediaAsset.Png)).Should().Be(HttpStatusCode.BadRequest);
        (await Request(_s.PatientAClient, _draft.Id, Photo, MediaAsset.WebP)).Should().Be(HttpStatusCode.BadRequest);
        (await Request(_s.PatientAClient, _draft.Id, Photo, "image/gif")).Should().Be(HttpStatusCode.BadRequest);
        (await Request(_s.PatientAClient, _draft.Id, _version.Q("exercise"), MediaAsset.Png)).Should().Be(HttpStatusCode.BadRequest);
        (await Request(_s.PatientBClient, _draft.Id, Photo, MediaAsset.Png)).Should().Be(HttpStatusCode.NotFound);
        (await Request(_s.DoctorAClient, _draft.Id, Photo, MediaAsset.Png)).Should().Be(HttpStatusCode.Forbidden);

        using (var submitted = await _s.PatientAClient.SubmitAsync(_draft.Id))
            submitted.StatusCode.Should().Be(HttpStatusCode.OK);
        (await Request(_s.PatientAClient, _draft.Id, Photo, MediaAsset.Png)).Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task InvalidMissingAndOversizedUploadsStayUnavailable()
    {
        var upload = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);

        using (var missing = await _s.PatientAClient.FinalizeAsync(upload.AssetId))
            (await missing.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("MediaAsset.Missing");
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, "GIF89a not a png"u8.ToArray());
        using (var invalid = await _s.PatientAClient.FinalizeAsync(upload.AssetId))
        {
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await invalid.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("MediaAsset.InvalidFormat");
        }
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, Png((int)MediaAsset.MaxImageBytes + 1));
        using (var oversized = await _s.PatientAClient.FinalizeAsync(upload.AssetId))
            (await oversized.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("MediaAsset.TooLarge");

        using (var reference = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, _draft.Revision,
                   [.. RequiredAnswers(_version), new AnswerDto(Photo, MediaAssetId: upload.AssetId)]))
            reference.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a pending asset cannot satisfy an answer");

        // A valid re-upload before expiry recovers the same asset.
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, Png());
        var ready = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();
        ready.State.Should().Be(MediaAssetState.Ready);

        var expired = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);
        await UploadAsync(expired.UploadUrl, MediaAsset.Png, Png());
        await SetAsync(expired.AssetId, "\"UploadExpiresAt\" = now() - interval '25 hours'");
        using var tooLate = await _s.PatientAClient.FinalizeAsync(expired.AssetId);
        tooLate.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ConcurrentFinalizationKeepsOneLogicalAssetAndOneFinalObject()
    {
        var upload = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, Png(256 * 1024));

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _s.PatientAClient.FinalizeAsync(upload.AssetId)));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.OK || r.StatusCode == HttpStatusCode.Conflict);
        var ready = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();

        ready.State.Should().Be(MediaAssetState.Ready);
        (await FinalObjectsAsync(upload.AssetId)).Should().ContainSingle();
        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task RecoversAnAbandonedPromotion()
    {
        var upload = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);
        await UploadAsync(upload.UploadUrl, MediaAsset.Png, Png());
        // Simulate an attempt that wrote its final object and then stopped before the asset became ready.
        var abandoned = $"final/{upload.AssetId:N}/abandoned";
        using (var scope = fixture.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IObjectStorage>().PutAsync(abandoned, new MemoryStream(Png()), MediaAsset.Png, Ct);
        await SetAsync(upload.AssetId, $"\"PromotionKey\" = '{abandoned}'");

        var ready = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();

        ready.State.Should().Be(MediaAssetState.Ready);
        var objects = await FinalObjectsAsync(upload.AssetId);
        objects.Should().ContainSingle().Which.Should().NotBe(abandoned);
    }

    [Fact]
    public async Task SubmissionAttachesReadyAssetsAtomically()
    {
        var photo = await _s.PatientAClient.UploadReadyPngAsync(_draft.Id, Photo);
        var otherEntry = await (await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today.AddDays(-1), null)).ReadAsync<EntryDto>();
        var foreign = await _s.PatientAClient.UploadReadyPngAsync(otherEntry.Id, Photo);

        using (var wrongEntry = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, _draft.Revision,
                   [.. RequiredAnswers(_version), new AnswerDto(Photo, MediaAssetId: foreign.Id)]))
            wrongEntry.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var saved = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, _draft.Revision,
            [.. RequiredAnswers(_version), new AnswerDto(Photo, MediaAssetId: photo.Id)]);
        var draft = await saved.ReadAsync<EntryDto>();

        // The asset expires before submission: submission fails and neither the entry nor the asset changes.
        await SetAsync(photo.Id, "\"State\" = 3, \"ExpiredAt\" = now()");
        using (var rejected = await _s.PatientAClient.SubmitAsync(draft.Id))
        {
            rejected.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await rejected.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("AttachmentUnavailable");
        }
        (await GetAssetAsync(photo.Id)).State.Should().Be(MediaAssetState.Expired);
        (await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct))!.Status.Should().Be(EntryStatus.Draft);

        await SetAsync(photo.Id, "\"State\" = 1, \"ExpiredAt\" = NULL");
        var submitted = await (await _s.PatientAClient.SubmitAsync(draft.Id)).ReadAsync<EntryDto>();

        submitted.Status.Should().Be(EntryStatus.Submitted);
        var attached = await GetAssetAsync(photo.Id);
        attached.State.Should().Be(MediaAssetState.Attached);
        attached.AttachedAt.Should().Be(submitted.SubmittedAt);
        (await GetAssetAsync(foreign.Id)).State.Should().Be(MediaAssetState.Ready);
    }

    [Fact]
    public async Task CleanupExpiresAbandonedMediaAndPreservesAttachedMedia()
    {
        var pending = await _s.PatientAClient.RequestUploadAsync(_draft.Id, Photo, MediaAsset.Png);
        await UploadAsync(pending.UploadUrl, MediaAsset.Png, Png());
        var unattached = await _s.PatientAClient.UploadReadyPngAsync(_draft.Id, Photo);
        var attachedAsset = await _s.PatientAClient.UploadReadyPngAsync(_draft.Id, Photo);
        using (var saved = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, _draft.Revision,
                   [.. RequiredAnswers(_version), new AnswerDto(Photo, MediaAssetId: attachedAsset.Id)]))
            (await (await _s.PatientAClient.SubmitAsync((await saved.ReadAsync<EntryDto>()).Id)).ReadAsync<EntryDto>()).Status.Should().Be(EntryStatus.Submitted);

        await SetAsync(pending.AssetId, "\"UploadExpiresAt\" = now() - interval '25 hours'");
        await SetAsync(unattached.Id, "\"ReadyAt\" = now() - interval '8 days'");
        await SetAsync(attachedAsset.Id, "\"ReadyAt\" = now() - interval '8 days'");

        // The first run fails to delete one object. Expiry still commits, and the next run finishes the deletion.
        var failing = new FailingOnceStorage(fixture.Garage.CreateStorage());
        var first = await RunCleanupAsync(failing);
        first.Expired.Should().Be(2);
        first.Failures.Should().Be(1);
        var second = await RunCleanupAsync(failing);
        second.Failures.Should().Be(0);

        var expiredPending = await GetAssetAsync(pending.AssetId);
        var expiredReady = await GetAssetAsync(unattached.Id);
        expiredPending.State.Should().Be(MediaAssetState.Expired);
        expiredReady.State.Should().Be(MediaAssetState.Expired);
        expiredPending.ObjectsDeletedAt.Should().NotBeNull();
        expiredReady.ObjectsDeletedAt.Should().NotBeNull();
        using var storage = fixture.Garage.CreateStorage();
        (await storage.GetInfoAsync(expiredPending.StagingKey, Ct)).Should().BeNull();
        (await storage.GetInfoAsync(expiredReady.FinalKey!, Ct)).Should().BeNull();

        var kept = await GetAssetAsync(attachedAsset.Id);
        kept.State.Should().Be(MediaAssetState.Attached);
        (await storage.GetInfoAsync(kept.FinalKey!, Ct)).Should().NotBeNull();
        (await storage.GetInfoAsync(kept.StagingKey, Ct)).Should().BeNull("finalization or cleanup removes promoted staging copies");
        using var download = await _s.DoctorAClient.GetAsync($"/api/media-assets/{attachedAsset.Id}/download-url", Ct);
        download.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CleanupAndSubmissionNeverBothClaimAnAsset()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        for (var round = 0; round < 6; round++)
        {
            // Each round races cleanup against submission for an asset that cleanup considers abandoned.
            var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId);
            var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
            var entry = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, null, RequiredAnswers(version))).ReadAsync<EntryDto>();
            var photo = await _s.PatientAClient.UploadReadyPngAsync(entry.Id, version.Q("photo"));
            var draft = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, entry.Revision,
                [.. RequiredAnswers(version), new AnswerDto(version.Q("photo"), MediaAssetId: photo.Id)])).ReadAsync<EntryDto>();
            await SetAsync(photo.Id, "\"ReadyAt\" = now() - interval '8 days'");

            var cleanup = RunCleanupAsync(fixture.Garage.CreateStorage());
            using var submit = await _s.PatientAClient.SubmitAsync(draft.Id);
            await cleanup;

            var asset = await GetAssetAsync(photo.Id);
            var stored = await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct);
            if (submit.StatusCode == HttpStatusCode.OK)
            {
                asset.State.Should().Be(MediaAssetState.Attached);
                using var storage = fixture.Garage.CreateStorage();
                (await storage.GetInfoAsync(asset.FinalKey!, Ct)).Should().NotBeNull();
            }
            else
            {
                submit.StatusCode.Should().Be(HttpStatusCode.BadRequest);
                asset.State.Should().Be(MediaAssetState.Expired);
                stored!.Status.Should().Be(EntryStatus.Draft);
            }
        }
    }

    private async Task<MediaCleanupResult> RunCleanupAsync(IObjectStorage storage)
    {
        using var scope = fixture.CreateScope();
        var cleanup = new MediaCleanup(scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(), storage, TimeProvider.System,
            Options.Create(new MediaCleanupOptions()), NullLogger<MediaCleanup>.Instance);
        return await cleanup.RunOnceAsync(Ct);
    }

    private async Task SetAsync(Guid assetId, string assignments)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
#pragma warning disable EF1002 // Test-only values written by the test itself.
        await db.Database.ExecuteSqlRawAsync($"UPDATE \"MediaAssets\" SET {assignments} WHERE \"Id\" = {{0}}", [assetId], Ct);
#pragma warning restore EF1002
    }

    private async Task<MediaAsset> GetAssetAsync(Guid assetId)
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().MediaAssets.AsNoTracking()
            .SingleAsync(a => a.Id == MediaAssetId.From(assetId), Ct);
    }

    private async Task<List<string>> FinalObjectsAsync(Guid assetId)
    {
        using var s3 = GarageReconciler.CreateS3Client(fixture.Garage.S3Endpoint, fixture.Garage.AccessKeyId, fixture.Garage.SecretAccessKey);
        var listed = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = GarageContainer.Bucket, Prefix = $"final/{assetId:N}/" }, Ct);
        return (listed.S3Objects ?? []).Select(o => o.Key).ToList();
    }

    /// <summary>
    /// Fails the first delete, then behaves normally.
    /// </summary>
    private sealed class FailingOnceStorage(IObjectStorage inner) : IObjectStorage
    {
        private int _failed;

        public Task DeleteAsync(string key, CancellationToken cancellationToken) =>
            Interlocked.Exchange(ref _failed, 1) == 0 ? throw new HttpRequestException("Injected storage failure") : inner.DeleteAsync(key, cancellationToken);

        public Task<Uri> CreateUploadUrlAsync(string key, string contentType, TimeSpan lifetime, CancellationToken cancellationToken) => inner.CreateUploadUrlAsync(key, contentType, lifetime, cancellationToken);
        public Task<Uri> CreateDownloadUrlAsync(string key, TimeSpan lifetime, CancellationToken cancellationToken) => inner.CreateDownloadUrlAsync(key, lifetime, cancellationToken);
        public Task<StoredObjectInfo?> GetInfoAsync(string key, CancellationToken cancellationToken) => inner.GetInfoAsync(key, cancellationToken);
        public Task<long?> DownloadAsync(string key, Stream destination, long maxBytes, CancellationToken cancellationToken) => inner.DownloadAsync(key, destination, maxBytes, cancellationToken);
        public Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken) => inner.PutAsync(key, content, contentType, cancellationToken);
    }
}
