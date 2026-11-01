namespace MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;

public class GetMediaDownloadUrlSummary : Summary<GetMediaDownloadUrlEndpoint>
{
    public GetMediaDownloadUrlSummary()
    {
        Summary = "Get a temporary download URL";
        Description =
            "Returns a signed URL for the finalized file of a ready or attached asset. Authorization follows the asset's entry: " +
            "the patient, the owning doctor with treatment, or a granted doctor with treatment; other callers receive 404. " +
            "The URL expires after five minutes and cannot be revoked earlier, so request a new one for each view. " +
            "Files are never publicly accessible.";
    }
}
