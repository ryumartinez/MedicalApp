using System.Net.Http.Headers;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using DotNet.Testcontainers.Volumes;
using GarageInitializer;
using MedicalApp.Common.Storage.Garage;
using Microsoft.Extensions.Options;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Storage;

/// <summary>
/// Runs the pinned Garage image with the local configuration and the same initialization as Aspire.
/// Named volumes let a test replace the container and keep its metadata and data.
/// </summary>
public sealed class GarageContainer : IAsyncDisposable
{
    public const string Image = "dxflrs/garage:v2.4.1";
    public const string Bucket = "medicalapp-media";
    public const string Origin = "https://localhost:7255";

    private readonly IVolume _metadata = new VolumeBuilder().WithName($"medicalapp-test-garage-meta-{Guid.NewGuid():N}").Build();
    private readonly IVolume _data = new VolumeBuilder().WithName($"medicalapp-test-garage-data-{Guid.NewGuid():N}").Build();
    private readonly string _rpcSecret = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
    private IContainer _container = null!;

    public string AdminToken { get; } = Guid.NewGuid().ToString("N");
    public string AccessKeyId { get; } = "GK" + Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(12));
    public string SecretAccessKey { get; } = Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    public Uri S3Endpoint => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(3900)}");
    public Uri AdminEndpoint => new($"http://{_container.Hostname}:{_container.GetMappedPublicPort(3903)}");

    public GarageSettings Settings => new(Bucket, AccessKeyId, SecretAccessKey, [Origin], 1024L * 1024 * 1024);

    public async Task InitializeAsync()
    {
        await _metadata.CreateAsync();
        await _data.CreateAsync();
        await StartContainerAsync();
        await ReconcileAsync();
    }

    public async Task ReconcileAsync()
    {
        using var admin = CreateAdminClient();
        await GarageReconciler.ReconcileAsync(admin, S3Endpoint, Settings);
    }

    /// <summary>
    /// Replaces the container, as an Aspire restart does, while keeping both volumes.
    /// </summary>
    public async Task RestartAsync()
    {
        await _container.DisposeAsync();
        await StartContainerAsync();
    }

    public HttpClient CreateAdminClient()
    {
        var client = new HttpClient { BaseAddress = AdminEndpoint };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", AdminToken);
        return client;
    }

    public GarageObjectStorage CreateStorage() => new(Options.Create(new GarageStorageOptions
    {
        ServiceUrl = S3Endpoint.ToString(),
        Bucket = Bucket,
        AccessKeyId = AccessKeyId,
        SecretAccessKey = SecretAccessKey
    }));

    private async Task StartContainerAsync()
    {
        _container = new ContainerBuilder()
            .WithImage(Image)
            .WithPortBinding(3900, true)
            .WithPortBinding(3903, true)
            .WithEnvironment("GARAGE_RPC_SECRET", _rpcSecret)
            .WithEnvironment("GARAGE_ADMIN_TOKEN", AdminToken)
            .WithResourceMapping(new FileInfo(Path.Combine(AppContext.BaseDirectory, "garage.toml")), "/etc/")
            .WithVolumeMount(_metadata, "/var/lib/garage/meta")
            .WithVolumeMount(_data, "/var/lib/garage/data")
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("Admin API server listening"))
            .Build();
        await _container.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        await _metadata.DisposeAsync();
        await _data.DisposeAsync();
    }
}
