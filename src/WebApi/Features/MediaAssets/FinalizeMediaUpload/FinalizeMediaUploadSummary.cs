namespace MedicalApp.Features.MediaAssets.FinalizeMediaUpload;

public class FinalizeMediaUploadSummary : Summary<FinalizeMediaUploadEndpoint>
{
    public FinalizeMediaUploadSummary()
    {
        Summary = "Validate an upload and make it ready";
        Description =
            "Reads the uploaded staging file once, checks that it exists, is not empty, fits the size limit, and has the declared " +
            "format by its file signature, then copies exactly those bytes to a separate final key that no upload URL can write. " +
            "Only then does the asset become Ready, with its size and SHA-256 digest. A ready asset can be referenced by a draft " +
            "answer; submission attaches it. Finalization never attaches an asset by itself. " +
            "Retrying returns the same ready asset. A missing, oversized, or invalid file returns 400 and leaves the asset pending " +
            "so the file can be uploaded again before the URL expires. Finalization closes 24 hours after the URL expires (409). " +
            "A storage failure leaves the asset unavailable; retry the request.";
    }
}
