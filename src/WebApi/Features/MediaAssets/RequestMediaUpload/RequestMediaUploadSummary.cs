namespace MedicalApp.Features.MediaAssets.RequestMediaUpload;

public class RequestMediaUploadSummary : Summary<RequestMediaUploadEndpoint>
{
    public RequestMediaUploadSummary()
    {
        Summary = "Request a signed upload URL";
        Description =
            "Creates a pending media asset for an image or video question of the caller's draft entry on an open plan, and returns a " +
            "signed URL that accepts one PUT of the file to a server-chosen staging key for 15 minutes. Send the returned Content-Type. " +
            "Images may be JPEG, PNG, or WebP up to 10 MB; videos may be MP4 up to 200 MB, limited further by the question's allowed types. " +
            "The URL cannot read files or write anywhere else, and it can never change a finalized file. " +
            "After uploading, call finalize; the asset is not usable until then.";
    }
}
