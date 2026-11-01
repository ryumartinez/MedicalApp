using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.MediaAssets.RequestMediaUpload;

public class RequestMediaUploadEndpoint(
    ApplicationDbContext dbContext,
    ICurrentIdentityService identities,
    IObjectStorage storage,
    TimeProvider timeProvider) : Endpoint<RequestMediaUploadRequest, RequestMediaUploadResponse>
{
    public override void Configure()
    {
        Post("/uploads");
        Group<MediaAssetsGroup>();
        Policies(ProgressPolicies.Patient);
        Description(x => x.WithName("RequestMediaUpload")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(RequestMediaUploadRequest req, CancellationToken ct)
    {
        var patientId = await identities.GetPatientIdAsync(ct);
        var entryId = ProgressEntryId.From(req.EntryId);
        var entry = await dbContext.ProgressEntries.AsNoTracking().SingleOrDefaultAsync(e => e.Id == entryId && e.PatientId == patientId, ct);
        if (entry is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var plan = await dbContext.ProgressPlans.AsNoTracking().SingleAsync(p => p.Id == entry.PlanId, ct);
        if (entry.IsSubmitted || !plan.IsOpen)
        {
            await HttpContext.SendErrorsAsync([entry.IsSubmitted ? ProgressEntryErrors.AlreadySubmitted : ProgressEntryErrors.PlanClosed], ct);
            return;
        }

        var version = await dbContext.ProgressFormVersions.AsNoTracking().SingleAsync(v => v.Id == entry.FormVersionId, ct);
        var question = version.FindQuestion(ProgressQuestionId.From(req.QuestionId));
        var contentType = req.ContentType.Trim().ToLowerInvariant();
        if (question is not { IsAttachment: true })
        {
            await HttpContext.SendErrorsAsync([MediaAssetErrors.NotAttachmentQuestion], ct);
            return;
        }
        if (!question.AllowedContentTypes.Contains(contentType))
        {
            await HttpContext.SendErrorsAsync([MediaAssetErrors.ContentTypeNotAllowed], ct);
            return;
        }

        // The server chooses the staging key. The signed URL can only write that key.
        var asset = MediaAsset.Create(patientId, entry.Id, question.Id, contentType, timeProvider.GetUtcNow());
        dbContext.MediaAssets.Add(asset);
        await dbContext.SaveChangesAsync(ct);

        var url = await storage.CreateUploadUrlAsync(asset.StagingKey, contentType, MediaAsset.UploadUrlLifetime, ct);
        await Send.OkAsync(new RequestMediaUploadResponse(asset.Id.Value, url, "PUT",
            new Dictionary<string, string> { ["Content-Type"] = contentType }, asset.MaxBytes, asset.UploadExpiresAt), ct);
    }
}
