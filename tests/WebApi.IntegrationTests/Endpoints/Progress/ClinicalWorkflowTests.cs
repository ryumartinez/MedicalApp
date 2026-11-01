using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Features.MediaAssets;
using MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;
using MedicalApp.Features.ProgressEntries;
using MedicalApp.Features.ProgressEntries.GetProgressHistory;
using MedicalApp.Features.ProgressEntries.GetProgressSummary;
using MedicalApp.Features.ProgressForms;
using MedicalApp.IntegrationTests.Common;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

/// <summary>
/// The three first-release workflows, driven only through the documented API.
/// </summary>
[Collection<ProgressApiCollection>]
public sealed class ClinicalWorkflowTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ProgressScenario.CreateAsync(fixture);

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task DailyPsychologyTracking()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync([
            new QuestionDto(null, "mood", QuestionType.NumericScale, "Mood", null, true, 0, 10, 1, "Very low", "Very good"),
            new QuestionDto(null, "energy", QuestionType.NumericScale, "Energy", null, true, 0, 10, 1),
            new QuestionDto(null, "sleep", QuestionType.SingleChoice, "Sleep", null, true,
                Options: [new QuestionDto.OptionDto(null, "Poorly"), new QuestionDto.OptionDto(null, "Well")]),
            new QuestionDto(null, "notes", QuestionType.Text, "Notes", null, false)]);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("America/Santiago")).DateTime);
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, ScheduleFrequency.Daily,
            "America/Santiago", localToday.AddDays(-2));
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        var sleep = version.Questions.Single(q => q.Key == "sleep");

        foreach (var (offset, mood) in new[] { (-2, 3), (-1, 5) })
        {
            var period = localToday.AddDays(offset);
            var draft = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, period, null,
                new AnswerDto(version.Q("mood"), Number: mood), new AnswerDto(version.Q("energy"), Number: 6),
                new AnswerDto(sleep.Id!.Value, OptionId: sleep.Options![1].Id))).ReadAsync<EntryDto>();
            (await (await _s.PatientAClient.SubmitAsync(draft.Id)).ReadAsync<EntryDto>()).IsLate.Should().BeTrue();
        }

        var summary = await _s.DoctorAClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?planId={plan.Id}", Ct);
        summary!.Plans.Single().Should().Match<GetProgressSummaryResponse.PlanSummaryDto>(p => p.Completed == 2 && p.Due == 1);
        summary.NumericTrends.Single(t => t.QuestionKey == "mood").Points.Select(p => p.Value).Should().Equal(3, 5);
    }

    [Fact]
    public async Task WeeklyStructuredPhysicalTherapyWithImage()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync([
            new QuestionDto(null, "pain", QuestionType.NumericScale, "Average pain", null, true, 0, 10),
            new QuestionDto(null, "range", QuestionType.SingleChoice, "Full bend?", null, true,
                Options: [new QuestionDto.OptionDto(null, "No"), new QuestionDto.OptionDto(null, "Yes")]),
            new QuestionDto(null, "photo", QuestionType.Image, "Photo of the knee", null, true, AllowedContentTypes: [MediaAsset.Jpeg, MediaAsset.Png])]);
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, ScheduleFrequency.Weekly, "UTC", Today.AddDays(-3));
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        var range = version.Questions.Single(q => q.Key == "range");

        var draft = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today.AddDays(-3), null,
            new AnswerDto(version.Q("pain"), Number: 4), new AnswerDto(range.Id!.Value, OptionId: range.Options![0].Id))).ReadAsync<EntryDto>();
        var jpeg = new byte[2048];
        new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }.CopyTo(jpeg, 0);
        var upload = await _s.PatientAClient.RequestUploadAsync(draft.Id, version.Q("photo"), MediaAsset.Jpeg);
        (await UploadAsync(upload.UploadUrl, MediaAsset.Jpeg, jpeg)).Status.Should().Be(HttpStatusCode.OK);
        var photo = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();
        var withPhoto = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today.AddDays(-3), draft.Revision,
            [.. draft.Answers, new AnswerDto(version.Q("photo"), MediaAssetId: photo.Id)])).ReadAsync<EntryDto>();
        var submitted = await (await _s.PatientAClient.SubmitAsync(withPhoto.Id)).ReadAsync<EntryDto>();

        submitted.IsLate.Should().BeFalse("the weekly period is still open");
        submitted.PeriodEndDate.Should().Be(Today.AddDays(3));
        var history = await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?planId={plan.Id}", Ct);
        var attachment = history!.Items.Single().Answers.Single(a => a.Type == QuestionType.Image).Attachment!;
        var url = await _s.DoctorAClient.GetFromJsonAsync<GetMediaDownloadUrlResponse>($"/api/media-assets/{attachment.Id}/download-url", Ct);
        using var http = new HttpClient();
        (await http.GetByteArrayAsync(url!.DownloadUrl, Ct)).Should().Equal(jpeg);
    }

    [Fact]
    public async Task WeeklyExerciseVideos()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync([
            new QuestionDto(null, "squat", QuestionType.Video, "Wall squats", null, true, AllowedContentTypes: [MediaAsset.Mp4]),
            new QuestionDto(null, "lunge", QuestionType.Video, "Lunges", null, true, AllowedContentTypes: [MediaAsset.Mp4])]);
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, ScheduleFrequency.Weekly, "UTC", Today);
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        var draft = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, null)).ReadAsync<EntryDto>();

        var answers = new List<AnswerDto>();
        foreach (var key in new[] { "squat", "lunge" })
        {
            var mp4 = new byte[64 * 1024];
            "\0\0\0\u0018ftypisom"u8.ToArray().CopyTo(mp4, 0);
            var upload = await _s.PatientAClient.RequestUploadAsync(draft.Id, version.Q(key), MediaAsset.Mp4);
            (await UploadAsync(upload.UploadUrl, MediaAsset.Mp4, mp4)).Status.Should().Be(HttpStatusCode.OK);
            var video = await (await _s.PatientAClient.FinalizeAsync(upload.AssetId)).ReadAsync<MediaAssetDto>();
            video.Kind.Should().Be(MediaKind.Video);
            answers.Add(new AnswerDto(version.Q(key), MediaAssetId: video.Id));
        }

        using (var incomplete = await _s.PatientAClient.SubmitAsync(draft.Id))
            incomplete.StatusCode.Should().Be(HttpStatusCode.BadRequest, "both videos are required");
        var saved = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, draft.Revision, [.. answers])).ReadAsync<EntryDto>();
        var submitted = await (await _s.PatientAClient.SubmitAsync(saved.Id)).ReadAsync<EntryDto>();

        submitted.Status.Should().Be(EntryStatus.Submitted);
        var history = await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?planId={plan.Id}", Ct);
        history!.Items.Single().Answers.Should().OnlyContain(a => a.Attachment!.State == MediaAssetState.Attached && a.Attachment.Kind == MediaKind.Video);
    }
}
