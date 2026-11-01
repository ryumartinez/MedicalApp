namespace MedicalApp.Features.MediaAssets.RequestMediaUpload;

/// <param name="QuestionId">The version-specific ID of an image or video question in the entry's version.</param>
/// <param name="ContentType">One of the question's allowed content types. The browser must send exactly this Content-Type.</param>
public record RequestMediaUploadRequest(Guid EntryId, Guid QuestionId, string ContentType);
