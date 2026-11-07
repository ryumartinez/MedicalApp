using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressEntries.GetProgressHistory;

public class GetProgressHistoryEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, ProgressAuthorizationService authorization)
    : Endpoint<GetProgressHistoryRequest, GetProgressHistoryResponse>
{
    public override void Configure()
    {
        Get("/history");
        Group<ProgressEntriesGroup>();
        Description(x => x.WithName("GetProgressHistory").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetProgressHistoryRequest req, CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var plans = ProgressScope.Plans(authorization, identity, req.PlanId, req.PatientId);
        if (plans.IsError)
        {
            await HttpContext.SendErrorsAsync(plans.Errors, ct);
            return;
        }

        var planIds = plans.Value.Select(plan => plan.Id);
        var entries = dbContext.ProgressEntries.AsNoTracking().Where(entry => planIds.Contains(entry.PlanId));
        if (req.From is { } from)
            entries = entries.Where(entry => entry.PeriodStartDate >= from);
        if (req.To is { } to)
            entries = entries.Where(entry => entry.PeriodStartDate <= to);
        if (req.Status is { } status)
            entries = entries.Where(entry => entry.Status == status);

        var total = await entries.CountAsync(ct);
        var page = await entries
            .OrderByDescending(entry => entry.PeriodStartDate).ThenBy(entry => entry.PlanId)
            .Skip((req.Page - 1) * req.PageSize).Take(req.PageSize)
            .ToListAsync(ct);

        var versionIds = page.Select(entry => entry.FormVersionId).Distinct().ToList();
        var versions = await dbContext.ProgressFormVersions.AsNoTracking().Where(v => versionIds.Contains(v.Id)).ToDictionaryAsync(v => v.Id, ct);
        var assetIds = page.SelectMany(entry => entry.Answers).Select(answer => answer.MediaAssetId).OfType<MediaAssetId>().ToList();
        var assets = await dbContext.MediaAssets.AsNoTracking().Where(a => assetIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, ct);

        var items = page.Select(entry =>
        {
            var version = versions[entry.FormVersionId];
            var answers = entry.Answers.Select(answer =>
            {
                var question = version.FindQuestion(answer.QuestionId)!;
                var option = answer.OptionId is { } optionId ? question.Options.FirstOrDefault(o => o.Id == optionId) : null;
                var asset = answer.MediaAssetId is { } assetId ? assets.GetValueOrDefault(assetId) : null;
                return new GetProgressHistoryResponse.HistoryAnswerDto(
                    question.Id.Value, question.Key, question.Label, question.Type, answer.Text, answer.Number,
                    option?.Id.Value, option?.Label,
                    asset is null ? null : new GetProgressHistoryResponse.AttachmentDto(asset.Id.Value, asset.Kind, asset.ContentType, asset.Size, asset.State));
            }).ToList();

            return new GetProgressHistoryResponse.HistoryEntryDto(entry.Id.Value, entry.PlanId.Value, entry.PatientId.Value,
                entry.PeriodStartDate, entry.PeriodEndDate, entry.PeriodStartUtc, entry.PeriodEndUtc, entry.Status, entry.SubmittedAt,
                entry.IsLate, new GetProgressHistoryResponse.FormVersionRefDto(version.Id.Value, version.FormId.Value, version.VersionNumber, version.Title),
                answers);
        }).ToList();

        await Send.OkAsync(new GetProgressHistoryResponse(items, total, req.Page, req.PageSize), ct);
    }
}
