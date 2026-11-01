namespace MedicalApp.Common.Storage.Garage;

public sealed class GarageStorageOptions
{
    public const string SectionName = "Storage:Garage";

    /// <summary>
    /// S3 endpoint the API uses for its own requests.
    /// </summary>
    public string? ServiceUrl { get; set; }

    /// <summary>
    /// S3 endpoint that browsers can reach. Signed URLs use it. Defaults to <see cref="ServiceUrl"/>.
    /// </summary>
    public string? PublicServiceUrl { get; set; }

    public string Region { get; set; } = "garage";

    public string? Bucket { get; set; }

    public string? AccessKeyId { get; set; }

    public string? SecretAccessKey { get; set; }
}
