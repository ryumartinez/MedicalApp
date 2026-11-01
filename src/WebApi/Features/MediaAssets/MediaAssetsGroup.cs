using MedicalApp.Common.Authorization;

namespace MedicalApp.Features.MediaAssets;

/// <summary>
/// Private image and video files. Every operation is authorized against the associated entry; others receive 404.
/// </summary>
public class MediaAssetsGroup : Group
{
    public MediaAssetsGroup()
    {
        Configure("media-assets", ep =>
        {
            ep.Policies(ProgressPolicies.DoctorOrPatient);
            ep.Description(x => x
                .ProducesProblemDetails(401)
                .ProducesProblemDetails(403)
                .ProducesProblemDetails(500));
        });
    }
}
