namespace AppHost.Garage;

/// <summary>
/// A single-node Garage container with an S3 endpoint and an internal admin endpoint.
/// </summary>
public sealed class GarageResource(string name) : ContainerResource(name)
{
    public const string S3EndpointName = "s3";
    public const string AdminEndpointName = "admin";

    public EndpointReference S3Endpoint => new(this, S3EndpointName);

    public EndpointReference AdminEndpoint => new(this, AdminEndpointName);
}

public static class GarageResourceBuilderExtensions
{
    public const int S3TargetPort = 3900;
    public const int AdminTargetPort = 3903;

    /// <summary>
    /// Adds Garage with persistent metadata and data volumes. Secrets reach the container as environment variables.
    /// </summary>
    /// <param name="s3Port">Stable host port, so signed browser URLs keep working across restarts.</param>
    public static IResourceBuilder<GarageResource> AddGarage(
        this IDistributedApplicationBuilder builder,
        string name,
        IResourceBuilder<ParameterResource> rpcSecret,
        IResourceBuilder<ParameterResource> adminToken,
        int s3Port = 3900,
        string? metadataVolume = null,
        string? dataVolume = null)
    {
        var configuration = File.ReadAllText(Path.Combine(builder.AppHostDirectory, "Garage", "garage.toml"));

        return builder.AddResource(new GarageResource(name))
            .WithImage(GarageImage.Image, GarageImage.Tag)
            .WithImageRegistry(GarageImage.Registry)
            .WithHttpEndpoint(port: s3Port, targetPort: S3TargetPort, name: GarageResource.S3EndpointName)
            // The admin endpoint stays on a random host port and only the initializer uses it.
            .WithHttpEndpoint(targetPort: AdminTargetPort, name: GarageResource.AdminEndpointName)
            .WithEnvironment("GARAGE_RPC_SECRET", rpcSecret)
            .WithEnvironment("GARAGE_ADMIN_TOKEN", adminToken)
            .WithContainerFiles("/etc", [new ContainerFile { Name = "garage.toml", Contents = configuration }])
            .WithVolume(metadataVolume ?? $"{builder.Environment.ApplicationName.ToLowerInvariant()}-{name}-meta", "/var/lib/garage/meta")
            .WithVolume(dataVolume ?? $"{builder.Environment.ApplicationName.ToLowerInvariant()}-{name}-data", "/var/lib/garage/data")
            // Healthy only once the layout gives the cluster quorum.
            .WithHttpHealthCheck("/health", endpointName: GarageResource.AdminEndpointName);
    }
}
