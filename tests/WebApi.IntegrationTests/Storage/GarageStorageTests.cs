using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Amazon.S3;
using GarageInitializer;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Storage;
using MedicalApp.IntegrationTests.Common.Infrastructure.Storage;

namespace MedicalApp.IntegrationTests.Storage;

public sealed class GarageStorageTests : IAsyncLifetime
{
    private readonly GarageContainer _garage = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await _garage.InitializeAsync();

    [Fact]
    public async Task InitializationIsIdempotentAndKeepsKeysScoped()
    {
        await _garage.ReconcileAsync();
        await _garage.ReconcileAsync();

        using var admin = _garage.CreateAdminClient();
        var layout = await admin.GetFromJsonAsync<JsonObject>("v2/GetClusterLayout", Ct);
        layout!["version"]!.GetValue<int>().Should().Be(1);
        var buckets = await admin.GetFromJsonAsync<JsonArray>("v2/ListBuckets", Ct);
        buckets!.Should().ContainSingle();
        var keys = await admin.GetFromJsonAsync<JsonArray>("v2/ListKeys", Ct);
        keys!.Select(x => x!["name"]!.GetValue<string>()).Should().BeEquivalentTo([GarageReconciler.ApiKeyName, GarageReconciler.InitKeyName]);

        var apiKey = await admin.GetFromJsonAsync<JsonObject>($"v2/GetKeyInfo?id={_garage.AccessKeyId}", Ct);
        apiKey!["permissions"]!["createBucket"]!.GetValue<bool>().Should().BeFalse();
        var permissions = apiKey["buckets"]!.AsArray().Single()!["permissions"]!;
        permissions["read"]!.GetValue<bool>().Should().BeTrue();
        permissions["write"]!.GetValue<bool>().Should().BeTrue();
        permissions["owner"]!.GetValue<bool>().Should().BeFalse();

        using var api = GarageReconciler.CreateS3Client(_garage.S3Endpoint, _garage.AccessKeyId, _garage.SecretAccessKey);
        var createBucket = () => api.PutBucketAsync("another-bucket", Ct);
        (await createBucket.Should().ThrowAsync<AmazonS3Exception>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var changeCors = () => api.DeleteCORSConfigurationAsync(GarageContainer.Bucket, Ct);
        (await changeCors.Should().ThrowAsync<AmazonS3Exception>()).Which.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task StoresReadsSignsAndRemovesPrivateObjects()
    {
        using var storage = _garage.CreateStorage();
        var bytes = RandomNumberGenerator.GetBytes(4096);
        await storage.PutAsync("final/object", new MemoryStream(bytes), "application/octet-stream", Ct);

        var info = await storage.GetInfoAsync("final/object", Ct);
        info!.Size.Should().Be(bytes.Length);
        info.ContentType.Should().Be("application/octet-stream");
        using (var copy = new MemoryStream())
        {
            (await storage.DownloadAsync("final/object", copy, bytes.Length, Ct)).Should().Be(bytes.Length);
            copy.ToArray().Should().Equal(bytes);
        }
        var tooSmall = () => storage.DownloadAsync("final/object", Stream.Null, bytes.Length - 1, Ct);
        await tooSmall.Should().ThrowAsync<ObjectTooLargeException>();

        using var http = new HttpClient();
        using (var anonymous = await http.GetAsync(new Uri(_garage.S3Endpoint, $"{GarageContainer.Bucket}/final/object"), Ct))
            anonymous.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var download = await storage.CreateDownloadUrlAsync("final/object", TimeSpan.FromSeconds(2), Ct);
        (await http.GetByteArrayAsync(download, Ct)).Should().Equal(bytes);
        await Task.Delay(TimeSpan.FromSeconds(3), Ct);
        using (var expired = await http.GetAsync(download, Ct))
            expired.IsSuccessStatusCode.Should().BeFalse();

        await storage.DeleteAsync("final/object", Ct);
        await storage.DeleteAsync("final/object", Ct);
        (await storage.GetInfoAsync("final/object", Ct)).Should().BeNull();
        (await storage.DownloadAsync("final/object", Stream.Null, 10, Ct)).Should().BeNull();
    }

    [Fact]
    public async Task BrowserUploadsThroughSignedUrlWithCors()
    {
        using var storage = _garage.CreateStorage();
        var upload = await storage.CreateUploadUrlAsync("staging/browser", "image/png", TimeSpan.FromMinutes(5), Ct);
        using var http = new HttpClient();

        using var preflight = new HttpRequestMessage(HttpMethod.Options, upload);
        preflight.Headers.Add("Origin", GarageContainer.Origin);
        preflight.Headers.Add("Access-Control-Request-Method", "PUT");
        preflight.Headers.Add("Access-Control-Request-Headers", "content-type");
        using var preflightResponse = await http.SendAsync(preflight, Ct);
        preflightResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        preflightResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GarageContainer.Origin);

        using var put = new HttpRequestMessage(HttpMethod.Put, upload) { Content = new ByteArrayContent([1, 2, 3]) };
        put.Content.Headers.ContentType = new("image/png");
        put.Headers.Add("Origin", GarageContainer.Origin);
        using var putResponse = await http.SendAsync(put, Ct);
        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        putResponse.Headers.GetValues("Access-Control-Allow-Origin").Should().Equal(GarageContainer.Origin);
        (await storage.GetInfoAsync("staging/browser", Ct))!.Size.Should().Be(3);

        // The signature covers one key and one content type. It grants no other access.
        using var otherKey = new HttpRequestMessage(HttpMethod.Put, upload.ToString().Replace("staging/browser", "final/browser"))
            { Content = new ByteArrayContent([9]) };
        otherKey.Content.Headers.ContentType = new("image/png");
        (await http.SendAsync(otherKey, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var otherType = new HttpRequestMessage(HttpMethod.Put, upload) { Content = new ByteArrayContent([9]) };
        otherType.Content.Headers.ContentType = new("text/html");
        (await http.SendAsync(otherType, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        using var read = await http.GetAsync(upload, Ct);
        read.IsSuccessStatusCode.Should().BeFalse();
        (await storage.GetInfoAsync("final/browser", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task PromotionStoresExactlyTheValidatedBytesDespiteStagingReplacement()
    {
        using var storage = _garage.CreateStorage();
        var promoter = new ExactContentPromoter(storage);
        var variants = Enumerable.Range(0, 8).Select(_ => RandomNumberGenerator.GetBytes(256 * 1024)).ToArray();
        await storage.PutAsync("staging/race", new MemoryStream(variants[0]), "image/png", Ct);

        // Replace the staging object continuously while promotions run.
        using var stop = new CancellationTokenSource();
        var replacer = Task.Run(async () =>
        {
            var index = 0;
            while (!stop.IsCancellationRequested)
                await storage.PutAsync("staging/race", new MemoryStream(variants[++index % variants.Length]), "image/png", CancellationToken.None);
        }, Ct);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            string? validatedHash = null;
            var result = await promoter.PromoteAsync("staging/race", $"final/race-{attempt}", "image/png", 1024 * 1024,
                async (content, _, ct) =>
                {
                    validatedHash = Convert.ToHexStringLower(await SHA256.HashDataAsync(content, ct));
                    return true;
                }, Ct);

            result.IsError.Should().BeFalse();
            result.Value.Sha256.Should().Be(validatedHash);
            using var stored = new MemoryStream();
            await storage.DownloadAsync(result.Value.FinalKey, stored, 1024 * 1024, Ct);
            Convert.ToHexStringLower(SHA256.HashData(stored.ToArray())).Should().Be(validatedHash);
            variants.Select(v => Convert.ToHexStringLower(SHA256.HashData(v))).Should().Contain(validatedHash);
        }

        await stop.CancelAsync();
        await replacer;

        // Later staging writes never reach a promoted object.
        using var before = new MemoryStream();
        await storage.DownloadAsync("final/race-0", before, 1024 * 1024, Ct);
        await storage.PutAsync("staging/race", new MemoryStream([1, 2, 3]), "image/png", Ct);
        using var after = new MemoryStream();
        await storage.DownloadAsync("final/race-0", after, 1024 * 1024, Ct);
        after.ToArray().Should().Equal(before.ToArray());
    }

    [Fact]
    public async Task PromotionRejectsMissingOversizedAndInvalidContent()
    {
        using var storage = _garage.CreateStorage();
        var promoter = new ExactContentPromoter(storage);
        await storage.PutAsync("staging/big", new MemoryStream(new byte[2048]), "image/png", Ct);

        var missing = await promoter.PromoteAsync("staging/none", "final/none", "image/png", 1024, (_, _, _) => Task.FromResult(true), Ct);
        var oversized = await promoter.PromoteAsync("staging/big", "final/big", "image/png", 1024, (_, _, _) => Task.FromResult(true), Ct);
        var invalid = await promoter.PromoteAsync("staging/big", "final/invalid", "image/png", 4096, (_, _, _) => Task.FromResult(false), Ct);

        missing.FirstError.Code.Should().Be(nameof(PromotionFailure.Missing));
        oversized.FirstError.Code.Should().Be(nameof(PromotionFailure.TooLarge));
        invalid.FirstError.Code.Should().Be(nameof(PromotionFailure.Invalid));
        (await storage.GetInfoAsync("final/big", Ct)).Should().BeNull();
        (await storage.GetInfoAsync("final/invalid", Ct)).Should().BeNull();
    }

    [Fact]
    public async Task MetadataAndBytesSurviveContainerReplacement()
    {
        using (var storage = _garage.CreateStorage())
            await storage.PutAsync("final/persisted", new MemoryStream([4, 5, 6]), "image/png", Ct);

        await _garage.RestartAsync();
        await _garage.ReconcileAsync();

        using var restarted = _garage.CreateStorage();
        using var copy = new MemoryStream();
        (await restarted.DownloadAsync("final/persisted", copy, 10, Ct)).Should().Be(3);
        copy.ToArray().Should().Equal([4, 5, 6]);
        using var admin = _garage.CreateAdminClient();
        (await admin.GetFromJsonAsync<JsonObject>("v2/GetClusterLayout", Ct))!["version"]!.GetValue<int>().Should().Be(1);
    }

    public async ValueTask DisposeAsync() => await _garage.DisposeAsync();
}
