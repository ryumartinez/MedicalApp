namespace MedicalApp.Common.Domain.MediaAssets;

public static class MediaAssetErrors
{
    public static readonly Error NotFound = Error.NotFound("MediaAsset.NotFound", "Media is not found");
    public static readonly Error NotAttachmentQuestion = Error.Validation("MediaAsset.NotAttachmentQuestion", "The question does not accept a file");
    public static readonly Error ContentTypeNotAllowed = Error.Validation("MediaAsset.ContentTypeNotAllowed", "The question does not accept this content type");
    public static readonly Error Expired = Error.Conflict("MediaAsset.Expired", "The upload expired. Request a new upload");
    public static readonly Error Missing = Error.Validation("MediaAsset.Missing", "No uploaded file was found. Upload the file before finalizing");
    public static readonly Error TooLarge = Error.Validation("MediaAsset.TooLarge", "The uploaded file exceeds the size limit");
    public static readonly Error Empty = Error.Validation("MediaAsset.Empty", "The uploaded file is empty");
    public static readonly Error InvalidFormat = Error.Validation("MediaAsset.InvalidFormat", "The uploaded file does not match its declared format");
    public static readonly Error Conflict = Error.Conflict("MediaAsset.Conflict", "Another request changed the upload. Retry finalization");
    public static readonly Error NotAvailable = Error.NotFound("MediaAsset.NotAvailable", "The media has no finalized file");
}
