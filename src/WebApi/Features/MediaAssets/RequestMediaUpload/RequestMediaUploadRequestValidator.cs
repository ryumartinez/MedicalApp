namespace MedicalApp.Features.MediaAssets.RequestMediaUpload;

public class RequestMediaUploadRequestValidator : Validator<RequestMediaUploadRequest>
{
    public RequestMediaUploadRequestValidator()
    {
        RuleFor(v => v.EntryId).NotEmpty();
        RuleFor(v => v.QuestionId).NotEmpty();
        RuleFor(v => v.ContentType).NotEmpty().MaximumLength(100);
    }
}
