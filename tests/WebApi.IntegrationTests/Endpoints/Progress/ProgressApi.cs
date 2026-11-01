using System.Net.Http.Json;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Features.ProgressForms;
using MedicalApp.Features.ProgressForms.CreateProgressForm;
using MedicalApp.Features.ProgressForms.PublishProgressForm;
using MedicalApp.Features.ProgressPlans;
using MedicalApp.Features.ProgressPlans.AssignProgressPlan;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Features.ProgressEntries;
using MedicalApp.Features.ProgressEntries.SaveProgressEntryDraft;
using MedicalApp.Features.ProgressPlans.GetProgressPlan;
using MedicalApp.Features.MediaAssets;
using MedicalApp.Features.MediaAssets.RequestMediaUpload;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

/// <summary>
/// Request helpers shared by progress endpoint tests.
/// </summary>
public static class ProgressApi
{
    public static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static IReadOnlyList<QuestionDto> AllTypes() =>
    [
        new(null, "notes", QuestionType.Text, "How was your day?", "A few words.", true),
        new(null, "mood", QuestionType.NumericScale, "Mood", null, true, 0, 10, 1, "Very low", "Very good"),
        new(null, "sleep", QuestionType.SingleChoice, "Sleep", null, false,
            Options: [new QuestionDto.OptionDto(null, "Poorly"), new QuestionDto.OptionDto(null, "Well")]),
        new(null, "photo", QuestionType.Image, "Photo of the area", null, false, AllowedContentTypes: [MediaAsset.Jpeg, MediaAsset.Png]),
        new(null, "exercise", QuestionType.Video, "Exercise video", null, false, AllowedContentTypes: [MediaAsset.Mp4])
    ];

    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{response.RequestMessage?.RequestUri} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    public static async Task<CreateProgressFormResponse> CreateFormAsync(this HttpClient doctor, IReadOnlyList<QuestionDto>? questions = null, string title = "Daily check-in")
    {
        using var response = await doctor.PostAsJsonAsync("/api/progress-forms", new CreateProgressFormRequest(title, null, questions ?? AllTypes()), Ct);
        return await response.ReadAsync<CreateProgressFormResponse>();
    }

    public static async Task<PublishProgressFormResponse> PublishAsync(this HttpClient doctor, Guid formId, uint revision)
    {
        using var response = await doctor.PostAsJsonAsync($"/api/progress-forms/{formId}/publish", new { Revision = revision }, Ct);
        return await response.ReadAsync<PublishProgressFormResponse>();
    }

    public static async Task<PlanDto> AssignAsync(this HttpClient doctor, Guid patientId, Guid versionId,
        ScheduleFrequency frequency = ScheduleFrequency.Daily, string timeZone = "UTC", DateOnly? start = null, DateOnly? end = null,
        Guid? replaces = null)
    {
        using var response = await doctor.PostAsJsonAsync("/api/progress-plans", new AssignProgressPlanRequest(
            patientId, versionId, frequency, timeZone, start ?? DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-3)), end, replaces), Ct);
        return await response.ReadAsync<PlanDto>();
    }

    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static async Task<GetProgressPlanResponse.FormVersionDto> GetVersionAsync(this HttpClient client, Guid planId) =>
        (await client.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{planId}", Ct))!.Form;

    public static Guid Q(this GetProgressPlanResponse.FormVersionDto version, string key) => version.Questions.Single(q => q.Key == key).Id!.Value;

    public static Task<HttpResponseMessage> SaveDraftAsync(this HttpClient patient, Guid planId, DateOnly period, uint? revision, params AnswerDto[] answers) =>
        patient.PutAsJsonAsync("/api/progress-entries/draft", new SaveProgressEntryDraftRequest(planId, period, revision, answers), Ct);

    public static Task<HttpResponseMessage> SubmitAsync(this HttpClient patient, Guid entryId, uint? revision = null) =>
        patient.PostAsync($"/api/progress-entries/{entryId}/submit" + (revision is null ? "" : $"?revision={revision}"), null, Ct);

    /// <summary>
    /// Answers every required question of <see cref="AllTypes"/>.
    /// </summary>
    public static AnswerDto[] RequiredAnswers(GetProgressPlanResponse.FormVersionDto version) =>
        [new(version.Q("notes"), Text: "A good day"), new(version.Q("mood"), Number: 7)];

    public static async Task<RequestMediaUploadResponse> RequestUploadAsync(this HttpClient patient, Guid entryId, Guid questionId, string contentType)
    {
        using var response = await patient.PostAsJsonAsync("/api/media-assets/uploads", new RequestMediaUploadRequest(entryId, questionId, contentType), Ct);
        return await response.ReadAsync<RequestMediaUploadResponse>();
    }

    public static async Task<HttpStatusCodeAndBody> UploadAsync(Uri url, string contentType, byte[] bytes)
    {
        using var http = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new(contentType);
        request.Headers.Add("Origin", "https://localhost:7255");
        using var response = await http.SendAsync(request, Ct);
        return new HttpStatusCodeAndBody(response.StatusCode, await response.Content.ReadAsStringAsync(Ct));
    }

    public static Task<HttpResponseMessage> FinalizeAsync(this HttpClient patient, Guid assetId) =>
        patient.PostAsync($"/api/media-assets/{assetId}/finalize", null, Ct);

    /// <summary>
    /// Uploads and finalizes a small valid PNG.
    /// </summary>
    public static async Task<MediaAssetDto> UploadReadyPngAsync(this HttpClient patient, Guid entryId, Guid questionId, byte[]? bytes = null)
    {
        var upload = await patient.RequestUploadAsync(entryId, questionId, MediaAsset.Png);
        (await UploadAsync(upload.UploadUrl, MediaAsset.Png, bytes ?? Png())).Status.Should().Be(System.Net.HttpStatusCode.OK);
        using var finalized = await patient.FinalizeAsync(upload.AssetId);
        return await finalized.ReadAsync<MediaAssetDto>();
    }

    public static byte[] Png(int size = 64)
    {
        var bytes = new byte[size];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes.AsSpan(8));
        return bytes;
    }

    public static async Task<PublishProgressFormResponse> CreatePublishedFormAsync(this HttpClient doctor, IReadOnlyList<QuestionDto>? questions = null)
    {
        var form = await doctor.CreateFormAsync(questions);
        return await doctor.PublishAsync(form.Id, form.Revision);
    }

}

public sealed record HttpStatusCodeAndBody(System.Net.HttpStatusCode Status, string Body);
