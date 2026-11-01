using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;

namespace MedicalApp.Features.ProgressEntries;

/// <summary>
/// An attachment answer must reference a ready asset of the same patient, entry, and version-specific question.
/// </summary>
public static class AttachmentEligibility
{
    public static List<Error> Check(ProgressEntry entry, IReadOnlyList<ProgressAnswer> answers, IReadOnlyCollection<MediaAsset> assets)
    {
        var byId = assets.ToDictionary(asset => asset.Id);
        var errors = new List<Error>();
        foreach (var (answer, index) in answers.Select((a, i) => (a, i)))
        {
            if (answer.MediaAssetId is not { } assetId)
                continue;
            if (!byId.TryGetValue(assetId, out var asset)
                || asset.PatientId != entry.PatientId
                || asset.EntryId != entry.Id
                || asset.QuestionId != answer.QuestionId
                || asset.State != MediaAssetState.Ready)
                errors.Add(ProgressEntryErrors.AttachmentUnavailable($"Answers[{index}]"));
        }

        return errors;
    }

    public static IReadOnlyList<MediaAssetId> AssetIds(IReadOnlyList<ProgressAnswer> answers) =>
        answers.Select(answer => answer.MediaAssetId).OfType<MediaAssetId>().ToList();
}
