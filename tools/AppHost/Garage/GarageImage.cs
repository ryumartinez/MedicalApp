namespace AppHost.Garage;

/// <summary>
/// Pinned Garage image. Validated with garage.toml, the admin API v2 initialization, and AWSSDK.S3 4.0.104.3.
/// </summary>
public static class GarageImage
{
    public const string Registry = "docker.io";
    public const string Image = "dxflrs/garage";
    public const string Tag = "v2.4.1";
}
