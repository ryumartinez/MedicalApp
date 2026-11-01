using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Features.MediaAssets.GetMediaDownloadUrl;
using MedicalApp.Features.ProgressEntries;
using MedicalApp.Features.ProgressEntries.GetProgressHistory;
using MedicalApp.Features.ProgressEntries.GetProgressSummary;
using MedicalApp.Features.ProgressForms;
using MedicalApp.Features.ProgressForms.GetProgressForm;
using MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;
using MedicalApp.Features.ProgressPlans;
using MedicalApp.IntegrationTests.Common;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

[Collection<ProgressApiCollection>]
public sealed class ProgressHistoryEndpointTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ProgressScenario.CreateAsync(fixture);

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task<EntryDto> SubmitPeriodAsync(PlanDto plan, DateOnly period, params AnswerDto[] answers)
    {
        using var saved = await _s.PatientAClient.SaveDraftAsync(plan.Id, period, null, answers);
        var draft = await saved.ReadAsync<EntryDto>();
        using var submitted = await _s.PatientAClient.SubmitAsync(draft.Id);
        return await submitted.ReadAsync<EntryDto>();
    }

    [Fact]
    public async Task CountsPeriodsWithTheSharedScheduleRules()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-4), end: Today.AddDays(2));
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        await SubmitPeriodAsync(plan, Today.AddDays(-4), RequiredAnswers(version));
        using (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today.AddDays(-3), null, new AnswerDto(version.Q("mood"), Number: 2))) { }
        await SubmitPeriodAsync(plan, Today.AddDays(-1), RequiredAnswers(version));
        var onTime = await SubmitPeriodAsync(plan, Today, RequiredAnswers(version));

        var summary = await _s.DoctorAClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?planId={plan.Id}", Ct);

        onTime.IsLate.Should().BeFalse();
        summary!.Plans.Single().Should().Be(new GetProgressSummaryResponse.PlanSummaryDto(
            plan.Id, published.VersionId, Completed: 3, CompletedLate: 2, Missed: 2, Due: 0, Closed: 0, Future: 2, IncompleteDrafts: 1));

        // Closing a plan turns its unfinished period into Closed rather than Missed, and leaves no future periods.
        var closing = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-1));
        using (var end = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{closing.Id}/end", new { closing.Revision }, Ct))
            end.StatusCode.Should().Be(HttpStatusCode.OK);
        var closed = (await _s.PatientAClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?planId={closing.Id}", Ct))!.Plans.Single();
        closed.Should().Match<GetProgressSummaryResponse.PlanSummaryDto>(p => p.Missed == 1 && p.Closed == 1 && p.Due == 0 && p.Future == 0);
        var open = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today);
        (await _s.PatientAClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?planId={open.Id}", Ct))!
            .Plans.Single().Should().Match<GetProgressSummaryResponse.PlanSummaryDto>(p => p.Due == 1 && p.Missed == 0 && p.Future == null);
    }

    [Fact]
    public async Task ReturnsFilteredPagedVersionAwareHistory()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-5));
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        var sleep = version.Questions.Single(q => q.Key == "sleep");
        for (var day = -5; day <= -1; day++)
            await SubmitPeriodAsync(plan, Today.AddDays(day), [.. RequiredAnswers(version), new AnswerDto(sleep.Id!.Value, OptionId: sleep.Options![1].Id)]);
        using (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, null, new AnswerDto(version.Q("mood"), Number: 5))) { }

        var all = await _s.PatientAClient.GetFromJsonAsync<GetProgressHistoryResponse>("/api/progress-entries/history?pageSize=4", Ct);
        all!.Total.Should().Be(6);
        all.Items.Should().HaveCount(4);
        all.Items[0].Status.Should().Be(EntryStatus.Draft);
        all.Items.Select(i => i.PeriodStartDate).Should().BeInDescendingOrder();
        var answer = all.Items[1].Answers.Single(a => a.QuestionKey == "sleep");
        answer.Should().Match<GetProgressHistoryResponse.HistoryAnswerDto>(a => a.OptionLabel == "Well" && a.Type == QuestionType.SingleChoice);
        all.Items[1].FormVersion.VersionNumber.Should().Be(1);
        all.Items[1].SubmittedAt.Should().NotBeNull();
        all.Items[1].IsLate.Should().BeTrue();

        var filtered = await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>(
            $"/api/progress-entries/history?patientId={_s.PatientA.Id.Value}&from={Today.AddDays(-4):yyyy-MM-dd}&to={Today.AddDays(-2):yyyy-MM-dd}&status=Submitted", Ct);
        filtered!.Items.Select(i => i.PeriodStartDate).Should().Equal(Today.AddDays(-2), Today.AddDays(-3), Today.AddDays(-4));
        (await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>("/api/progress-entries/history?pageSize=4&page=2&planId=" + plan.Id, Ct))!
            .Items.Should().HaveCount(2);

        // A later publication does not reinterpret earlier answers.
        var form = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{published.FormId}", Ct);
        var relabeled = form!.DraftQuestions.Select(q => q.Key == "sleep" ? q with { Options = [q.Options![0], q.Options[1] with { Label = "Excellent" }] } : q).ToList();
        using (var update = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{published.FormId}/draft",
                   new UpdateProgressFormDraftRequest(published.FormId, form.Revision, form.Title, null, relabeled), Ct))
            await _s.DoctorAClient.PublishAsync(published.FormId, (await update.ReadAsync<UpdateProgressFormDraftResponse>()).Revision);
        (await _s.PatientAClient.GetFromJsonAsync<GetProgressHistoryResponse>("/api/progress-entries/history?status=Submitted&pageSize=1", Ct))!
            .Items.Single().Answers.Single(a => a.QuestionKey == "sleep").OptionLabel.Should().Be("Well");
    }

    [Fact]
    public async Task AppliesAccessPoliciesToHistoryAndSummaries()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-1));
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        await SubmitPeriodAsync(plan, Today, RequiredAnswers(version));

        (await _s.DoctorBClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?planId={plan.Id}", Ct))!.Items.Should().BeEmpty();
        (await _s.PatientBClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?patientId={_s.PatientA.Id.Value}", Ct))!.Items.Should().BeEmpty();
        (await _s.PatientBClient.GetFromJsonAsync<GetProgressHistoryResponse>("/api/progress-entries/history", Ct))!.Items.Should().BeEmpty();
        (await _s.DoctorBClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?planId={plan.Id}", Ct))!.Plans.Should().BeEmpty();
        using var missingScope = await _s.DoctorAClient.GetAsync("/api/progress-entries/history", Ct);
        missingScope.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var anonymous = await fixture.CreateAnonymousClient().GetAsync("/api/progress-entries/summary", Ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?planId={plan.Id}", Ct))!.Items.Should().ContainSingle();
    }

    [Fact]
    public async Task KeepsChangedScalesAsSeparateTrends()
    {
        var created = await _s.DoctorAClient.CreateFormAsync();
        var v1 = await _s.DoctorAClient.PublishAsync(created.Id, created.Revision);
        var first = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, v1.VersionId, start: Today.AddDays(-1));
        var firstVersion = await _s.PatientAClient.GetVersionAsync(first.Id);
        await SubmitPeriodAsync(first, Today.AddDays(-1), new AnswerDto(firstVersion.Q("notes"), Text: "x"), new AnswerDto(firstVersion.Q("mood"), Number: 8));

        var form = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{created.Id}", Ct);
        var rescaled = form!.DraftQuestions.Select(q => q.Key == "mood" ? q with { Label = "Mood (1 to 5)", Min = 1, Max = 5, Step = 1 } : q).ToList();
        using var update = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{created.Id}/draft",
            new UpdateProgressFormDraftRequest(created.Id, form.Revision, form.Title, null, rescaled), Ct);
        var v2 = await _s.DoctorAClient.PublishAsync(created.Id, (await update.ReadAsync<UpdateProgressFormDraftResponse>()).Revision);
        var second = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, v2.VersionId, start: Today, replaces: first.Id);
        var secondVersion = await _s.PatientAClient.GetVersionAsync(second.Id);
        await SubmitPeriodAsync(second, Today, new AnswerDto(secondVersion.Q("notes"), Text: "y"), new AnswerDto(secondVersion.Q("mood"), Number: 4));

        var summary = await _s.DoctorAClient.GetFromJsonAsync<GetProgressSummaryResponse>($"/api/progress-entries/summary?patientId={_s.PatientA.Id.Value}", Ct);

        var trends = summary!.NumericTrends.Where(t => t.QuestionKey == "mood").ToList();
        trends.Should().HaveCount(2);
        trends[0].Should().Match<GetProgressSummaryResponse.NumericTrendDto>(t => t.VersionNumber == 1 && t.Min == 0 && t.Max == 10);
        trends[1].Should().Match<GetProgressSummaryResponse.NumericTrendDto>(t => t.VersionNumber == 2 && t.Min == 1 && t.Max == 5 && t.QuestionLabel == "Mood (1 to 5)");
        trends[0].QuestionId.Should().NotBe(trends[1].QuestionId);
        trends[0].Points.Single().Value.Should().Be(8);
        trends[1].Points.Single().Value.Should().Be(4);
        summary.Plans.Should().HaveCount(2);
    }

    [Fact]
    public async Task HistoryExposesAttachmentMetadataButNotContent()
    {
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today);
        var version = await _s.PatientAClient.GetVersionAsync(plan.Id);
        var draft = await (await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, null, RequiredAnswers(version))).ReadAsync<EntryDto>();
        var photo = await _s.PatientAClient.UploadReadyPngAsync(draft.Id, version.Q("photo"));
        using (var saved = await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, draft.Revision,
                   [.. RequiredAnswers(version), new AnswerDto(version.Q("photo"), MediaAssetId: photo.Id)]))
            (await _s.PatientAClient.SubmitAsync((await saved.ReadAsync<EntryDto>()).Id)).StatusCode.Should().Be(HttpStatusCode.OK);

        var history = await _s.DoctorAClient.GetFromJsonAsync<GetProgressHistoryResponse>($"/api/progress-entries/history?planId={plan.Id}", Ct);
        var raw = await _s.DoctorAClient.GetStringAsync($"/api/progress-entries/history?planId={plan.Id}", Ct);

        var attachment = history!.Items.Single().Answers.Single(a => a.Type == QuestionType.Image).Attachment!;
        attachment.Should().Be(new GetProgressHistoryResponse.AttachmentDto(photo.Id, MediaKind.Image, MediaAsset.Png, photo.Size, MediaAssetState.Attached));
        raw.Should().NotContain("X-Amz-Signature").And.NotContain("final/");
        var url = await _s.DoctorAClient.GetFromJsonAsync<GetMediaDownloadUrlResponse>($"/api/media-assets/{attachment.Id}/download-url", Ct);
        url!.DownloadUrl.Query.Should().Contain("X-Amz-Signature");
        using var denied = await _s.DoctorBClient.GetAsync($"/api/media-assets/{attachment.Id}/download-url", Ct);
        denied.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
