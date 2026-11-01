using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace GarageInitializer;

public sealed record GarageSettings(
    string Bucket,
    string ApiAccessKeyId,
    string ApiSecretAccessKey,
    IReadOnlyList<string> CorsOrigins,
    long NodeCapacityBytes = 100L * 1024 * 1024 * 1024);

/// <summary>
/// Initializes a single-node Garage cluster through the admin API v2. Every step checks existing state first,
/// so repeated runs and retries after a failure change nothing that is already correct.
/// </summary>
public static class GarageReconciler
{
    public const string InitKeyName = "medicalapp-init";
    public const string ApiKeyName = "medicalapp-api";
    public const string Region = "garage";

    /// <param name="admin">A client for the admin endpoint with the admin bearer token.</param>
    /// <param name="s3Endpoint">The S3 endpoint for CORS configuration and the readiness probe.</param>
    public static async Task ReconcileAsync(HttpClient admin, Uri s3Endpoint, GarageSettings settings, CancellationToken cancellationToken = default)
    {
        var nodeId = await WaitForNodeAsync(admin, cancellationToken);
        await EnsureLayoutAsync(admin, nodeId, settings.NodeCapacityBytes, cancellationToken);
        await WaitForHealthAsync(admin, cancellationToken);
        var bucketId = await EnsureBucketAsync(admin, settings.Bucket, cancellationToken);
        await EnsureApiKeyAsync(admin, bucketId, settings, cancellationToken);
        var (initKeyId, initSecret) = await EnsureInitKeyAsync(admin, bucketId, cancellationToken);

        using (var owner = CreateS3Client(s3Endpoint, initKeyId, initSecret))
            await EnsureCorsAsync(owner, settings, cancellationToken);

        using var api = CreateS3Client(s3Endpoint, settings.ApiAccessKeyId, settings.ApiSecretAccessKey);
        await ProbeAsync(api, settings.Bucket, cancellationToken);
    }

    public static AmazonS3Client CreateS3Client(Uri endpoint, string accessKeyId, string secretAccessKey) =>
        new(new BasicAWSCredentials(accessKeyId, secretAccessKey), new AmazonS3Config
        {
            ServiceURL = endpoint.ToString(),
            ForcePathStyle = true,
            AuthenticationRegion = Region,
            // Garage does not return flexible checksums, so only send them when an operation requires one.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED
        });

    private static async Task<string> WaitForNodeAsync(HttpClient admin, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var status = await admin.GetFromJsonAsync<JsonObject>("v2/GetClusterStatus", cancellationToken);
                var node = status?["nodes"]?.AsArray().SingleOrDefault();
                if (node?["id"]?.GetValue<string>() is { } id)
                    return id;
            }
            catch (HttpRequestException) when (attempt < 60)
            {
            }

            if (attempt >= 60)
                throw new InvalidOperationException("Garage did not report a single local node.");
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }
    }

    private static async Task EnsureLayoutAsync(HttpClient admin, string nodeId, long capacity, CancellationToken cancellationToken)
    {
        var layout = await admin.GetFromJsonAsync<JsonObject>("v2/GetClusterLayout", cancellationToken)
            ?? throw new InvalidOperationException("Garage returned an invalid layout.");
        if (layout["roles"]!.AsArray().Any(role => role?["id"]?.GetValue<string>() == nodeId))
            return;

        // A staged change can remain after an interrupted run. Staging the same role again is harmless.
        var staged = layout["stagedRoleChanges"]?.AsArray().Any(role => role?["id"]?.GetValue<string>() == nodeId) == true;
        if (!staged)
        {
            using var update = await admin.PostAsJsonAsync("v2/UpdateClusterLayout", new JsonObject
            {
                ["roles"] = new JsonArray(new JsonObject
                {
                    ["id"] = nodeId,
                    ["zone"] = "local",
                    ["capacity"] = capacity,
                    ["tags"] = new JsonArray()
                })
            }, cancellationToken);
            update.EnsureSuccessStatusCode();
        }

        using var apply = await admin.PostAsJsonAsync("v2/ApplyClusterLayout",
            new JsonObject { ["version"] = layout["version"]!.GetValue<long>() + 1 }, cancellationToken);
        apply.EnsureSuccessStatusCode();
    }

    private static async Task WaitForHealthAsync(HttpClient admin, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            using var response = await admin.GetAsync("health", cancellationToken);
            if (response.IsSuccessStatusCode)
                return;
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
        }

        throw new InvalidOperationException("Garage did not become healthy after the layout was applied.");
    }

    private static async Task<string> EnsureBucketAsync(HttpClient admin, string bucket, CancellationToken cancellationToken)
    {
        var path = $"v2/GetBucketInfo?globalAlias={Uri.EscapeDataString(bucket)}";
        using (var existing = await admin.GetAsync(path, cancellationToken))
        {
            if (existing.IsSuccessStatusCode)
                return await ReadBucketIdAsync(existing, cancellationToken);
            if (existing.StatusCode != HttpStatusCode.NotFound)
                existing.EnsureSuccessStatusCode();
        }

        using var create = await admin.PostAsJsonAsync("v2/CreateBucket", new JsonObject { ["globalAlias"] = bucket }, cancellationToken);
        if (create.StatusCode != HttpStatusCode.Conflict)
        {
            create.EnsureSuccessStatusCode();
            return await ReadBucketIdAsync(create, cancellationToken);
        }

        using var created = await admin.GetAsync(path, cancellationToken);
        created.EnsureSuccessStatusCode();
        return await ReadBucketIdAsync(created, cancellationToken);
    }

    private static async Task<string> ReadBucketIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var bucket = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        // Buckets stay private: website access is never enabled.
        if (bucket?["websiteAccess"]?.GetValue<bool>() == true)
            throw new InvalidOperationException("The media bucket must not allow website access.");
        return bucket?["id"]?.GetValue<string>() ?? throw new InvalidOperationException("Garage returned an invalid bucket.");
    }

    private static async Task EnsureApiKeyAsync(HttpClient admin, string bucketId, GarageSettings settings, CancellationToken cancellationToken)
    {
        using (var existing = await admin.GetAsync($"v2/GetKeyInfo?id={Uri.EscapeDataString(settings.ApiAccessKeyId)}", cancellationToken))
        {
            if (existing.StatusCode == HttpStatusCode.NotFound)
            {
                using var import = await admin.PostAsJsonAsync("v2/ImportKey", new JsonObject
                {
                    ["accessKeyId"] = settings.ApiAccessKeyId,
                    ["secretAccessKey"] = settings.ApiSecretAccessKey,
                    ["name"] = ApiKeyName
                }, cancellationToken);
                if (import.StatusCode != HttpStatusCode.Conflict)
                    import.EnsureSuccessStatusCode();
            }
            else
            {
                existing.EnsureSuccessStatusCode();
            }
        }

        // The API reads and writes objects in its bucket only. It cannot create buckets or change bucket settings.
        await SetKeyPermissionsAsync(admin, bucketId, settings.ApiAccessKeyId, read: true, write: true, owner: false, cancellationToken);
    }

    private static async Task<(string Id, string Secret)> EnsureInitKeyAsync(HttpClient admin, string bucketId, CancellationToken cancellationToken)
    {
        var keys = await admin.GetFromJsonAsync<JsonArray>("v2/ListKeys", cancellationToken) ?? [];
        var keyId = keys.Select(key => key!.AsObject())
            .Where(key => key["name"]?.GetValue<string>() == InitKeyName)
            .Select(key => key["id"]!.GetValue<string>())
            .Order(StringComparer.Ordinal)
            .FirstOrDefault();
        if (keyId is null)
        {
            using var create = await admin.PostAsJsonAsync("v2/CreateKey", new JsonObject { ["name"] = InitKeyName }, cancellationToken);
            create.EnsureSuccessStatusCode();
            keyId = (await create.Content.ReadFromJsonAsync<JsonObject>(cancellationToken))!["accessKeyId"]!.GetValue<string>();
        }

        var info = await admin.GetFromJsonAsync<JsonObject>($"v2/GetKeyInfo?id={Uri.EscapeDataString(keyId)}&showSecretKey=true", cancellationToken)
            ?? throw new InvalidOperationException("Garage returned an invalid key.");

        // The initializer key only manages bucket settings such as CORS. It cannot read or write objects.
        await SetKeyPermissionsAsync(admin, bucketId, keyId, read: false, write: false, owner: true, cancellationToken);
        return (keyId, info["secretAccessKey"]!.GetValue<string>());
    }

    private static async Task SetKeyPermissionsAsync(HttpClient admin, string bucketId, string keyId, bool read, bool write, bool owner,
        CancellationToken cancellationToken)
    {
        var deny = new JsonObject { ["read"] = !read, ["write"] = !write, ["owner"] = !owner };
        if (!read || !write || !owner)
        {
            using var denied = await admin.PostAsJsonAsync("v2/DenyBucketKey",
                new JsonObject { ["bucketId"] = bucketId, ["accessKeyId"] = keyId, ["permissions"] = deny }, cancellationToken);
            denied.EnsureSuccessStatusCode();
        }

        using var allowed = await admin.PostAsJsonAsync("v2/AllowBucketKey", new JsonObject
        {
            ["bucketId"] = bucketId,
            ["accessKeyId"] = keyId,
            ["permissions"] = new JsonObject { ["read"] = read, ["write"] = write, ["owner"] = owner }
        }, cancellationToken);
        allowed.EnsureSuccessStatusCode();
    }

    private static async Task EnsureCorsAsync(AmazonS3Client owner, GarageSettings settings, CancellationToken cancellationToken)
    {
        // Browsers upload with signed PUT URLs and download with signed GET URLs.
        await owner.PutCORSConfigurationAsync(new PutCORSConfigurationRequest
        {
            BucketName = settings.Bucket,
            Configuration = new CORSConfiguration
            {
                Rules =
                [
                    new CORSRule
                    {
                        Id = "medicalapp-browser",
                        AllowedOrigins = settings.CorsOrigins.ToList(),
                        AllowedMethods = ["PUT", "GET", "HEAD"],
                        AllowedHeaders = ["content-type", "content-length"],
                        ExposeHeaders = ["ETag"],
                        MaxAgeSeconds = 600
                    }
                ]
            }
        }, cancellationToken);
    }

    private static async Task ProbeAsync(AmazonS3Client api, string bucket, CancellationToken cancellationToken)
    {
        // Storage is ready only when the API key can write, read, and delete an object.
        var key = $".readiness/{Guid.NewGuid():N}";
        await api.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, ContentBody = "ready" }, cancellationToken);
        using (var read = await api.GetObjectAsync(bucket, key, cancellationToken))
        using (var reader = new StreamReader(read.ResponseStream))
        {
            if (await reader.ReadToEndAsync(cancellationToken) != "ready")
                throw new InvalidOperationException("The storage readiness probe read different content.");
        }
        await api.DeleteObjectAsync(bucket, key, cancellationToken);
    }
}
