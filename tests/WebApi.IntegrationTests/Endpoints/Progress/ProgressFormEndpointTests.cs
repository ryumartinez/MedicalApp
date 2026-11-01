using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Persistence;
using MedicalApp.Features.ProgressForms;
using MedicalApp.Features.ProgressForms.CreateProgressForm;
using MedicalApp.Features.ProgressForms.DuplicateProgressForm;
using MedicalApp.Features.ProgressForms.GetProgressForm;
using MedicalApp.Features.ProgressForms.GetProgressForms;
using MedicalApp.Features.ProgressForms.PublishProgressForm;
using MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

[Collection<ProgressApiCollection>]
public sealed class ProgressFormEndpointTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ProgressScenario.CreateAsync(fixture);

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task PublishesAllFiveTypesAndLaterVersionsKeepEarlierDefinitions()
    {
        var created = await _s.DoctorAClient.CreateFormAsync();
        var first = await _s.DoctorAClient.PublishAsync(created.Id, created.Revision);
        var form = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{created.Id}", Ct);

        var version = form!.Versions.Single();
        version.Questions.Select(q => q.Type).Should().Equal(QuestionType.Text, QuestionType.NumericScale, QuestionType.SingleChoice, QuestionType.Image, QuestionType.Video);
        version.Questions[1].Should().Match<QuestionDto>(q => q.Min == 0 && q.Max == 10 && q.Step == 1 && q.MinLabel == "Very low" && q.Required);
        version.Questions[3].AllowedContentTypes.Should().Equal("image/jpeg", "image/png");
        version.Questions[0].Instructions.Should().Be("A few words.");

        // Change a label and the option set, then publish again.
        var changed = form.DraftQuestions.Select(q => q.Key == "sleep"
            ? q with { Label = "Sleep quality", Options = [q.Options![0] with { Label = "Badly" }, new QuestionDto.OptionDto(null, "Great")] }
            : q).ToList();
        using (var update = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{created.Id}/draft",
                   new UpdateProgressFormDraftRequest(created.Id, form.Revision, "Daily check-in v2", null, changed), Ct))
        {
            var updated = await update.ReadAsync<UpdateProgressFormDraftResponse>();
            var second = await _s.DoctorAClient.PublishAsync(created.Id, updated.Revision);
            second.VersionNumber.Should().Be(2);
        }

        var after = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{created.Id}", Ct);
        after!.Versions.Should().HaveCount(2);
        after.Versions[0].Should().BeEquivalentTo(version);
        after.Versions[1].Questions.Select(q => q.Key).Should().Equal(version.Questions.Select(q => q.Key));
        after.Versions[1].Questions.Select(q => q.Id).Should().NotIntersectWith(version.Questions.Select(q => q.Id));
        after.Versions[1].Questions[2].Options![0].Should().Be(version.Questions[2].Options![0] with { Label = "Badly" });
        after.Versions[0].Title.Should().Be("Daily check-in");
        first.VersionNumber.Should().Be(1);
    }

    [Fact]
    public async Task InvalidPublicationCreatesNoVersion()
    {
        var created = await _s.DoctorAClient.CreateFormAsync([
            new QuestionDto(null, "mood", QuestionType.NumericScale, "", null, true, 5, 5),
            new QuestionDto(null, "sleep", QuestionType.SingleChoice, "Sleep", null, false, Options: [new QuestionDto.OptionDto(null, "Only")])]);

        using var publish = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-forms/{created.Id}/publish", new { created.Revision }, Ct);

        publish.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await publish.Content.ReadAsStringAsync(Ct);
        problem.Should().ContainEquivalentOf("ProgressForm.LabelRequired").And.ContainEquivalentOf("ProgressForm.InvalidScale").And.ContainEquivalentOf("ProgressForm.TooFewOptions");
        using var scope = fixture.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProgressFormVersions.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task EnforcesLimitsAndStaleRevisions()
    {
        using var tooLong = await _s.DoctorAClient.PostAsJsonAsync("/api/progress-forms",
            new CreateProgressFormRequest(new string('a', 201), null, []), Ct);
        tooLong.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var created = await _s.DoctorAClient.CreateFormAsync();
        using var firstUpdate = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{created.Id}/draft",
            new UpdateProgressFormDraftRequest(created.Id, created.Revision, "First", null, AllTypes()), Ct);
        firstUpdate.StatusCode.Should().Be(HttpStatusCode.OK);
        using var staleUpdate = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{created.Id}/draft",
            new UpdateProgressFormDraftRequest(created.Id, created.Revision, "Second", null, AllTypes()), Ct);
        using var stalePublish = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-forms/{created.Id}/publish", new { created.Revision }, Ct);

        staleUpdate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        stalePublish.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{created.Id}", Ct))!.Title.Should().Be("First");
    }

    [Fact]
    public async Task ConcurrentPublicationsTakeUniqueNumbers()
    {
        var created = await _s.DoctorAClient.CreateFormAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _s.DoctorAClient.PostAsJsonAsync($"/api/progress-forms/{created.Id}/publish", new { created.Revision }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        var form = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{created.Id}", Ct);
        form!.Versions.Select(v => v.VersionNumber).Should().Equal(1);
        foreach (var response in responses)
            response.Dispose();
    }

    [Fact]
    public async Task DuplicatesAreIndependent()
    {
        var source = await _s.DoctorAClient.CreateFormAsync();
        await _s.DoctorAClient.PublishAsync(source.Id, source.Revision);
        using var duplicateResponse = await _s.DoctorAClient.PostAsync($"/api/progress-forms/{source.Id}/duplicate", null, Ct);
        var duplicate = await duplicateResponse.ReadAsync<DuplicateProgressFormResponse>();

        using (var update = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{duplicate.Id}/draft",
                   new UpdateProgressFormDraftRequest(duplicate.Id, duplicate.Revision, "For Alice", null, AllTypes().Take(2).ToList()), Ct))
        {
            var updated = await update.ReadAsync<UpdateProgressFormDraftResponse>();
            (await _s.DoctorAClient.PublishAsync(duplicate.Id, updated.Revision)).VersionNumber.Should().Be(1);
        }

        var original = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{source.Id}", Ct);
        original!.Title.Should().Be("Daily check-in");
        original.DraftQuestions.Should().HaveCount(5);
        original.Versions.Should().ContainSingle();
        var copy = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{duplicate.Id}", Ct);
        copy!.SourceFormId.Should().Be(source.Id);
        copy.Versions.Single().Questions.Should().HaveCount(2);
    }

    [Fact]
    public async Task ScopesFormsToTheirOwner()
    {
        var mine = await _s.DoctorAClient.CreateFormAsync(title: "Mood diary");
        await _s.DoctorAClient.CreateFormAsync(title: "Knee therapy");
        var theirs = await _s.DoctorBClient.CreateFormAsync(title: "Mood scale");

        var list = await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormsResponse>("/api/progress-forms?search=MOOD", Ct);
        list!.Items.Select(f => f.Id).Should().Equal(mine.Id);
        list.Total.Should().Be(1);
        (await _s.DoctorAClient.GetFromJsonAsync<GetProgressFormsResponse>("/api/progress-forms?pageSize=1&page=2", Ct))!
            .Items.Single().Title.Should().Be("Mood diary");

        using var otherDetail = await _s.DoctorAClient.GetAsync($"/api/progress-forms/{theirs.Id}", Ct);
        using var otherUpdate = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{theirs.Id}/draft",
            new UpdateProgressFormDraftRequest(theirs.Id, theirs.Revision, "Mine now", null, []), Ct);
        using var otherPublish = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-forms/{theirs.Id}/publish", new { theirs.Revision }, Ct);
        using var otherDuplicate = await _s.DoctorAClient.PostAsync($"/api/progress-forms/{theirs.Id}/duplicate", null, Ct);
        using var patient = await _s.PatientAClient.GetAsync("/api/progress-forms", Ct);
        using var anonymous = await fixture.CreateAnonymousClient().GetAsync("/api/progress-forms", Ct);

        otherDetail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherPublish.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherDuplicate.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patient.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.DoctorBClient.GetFromJsonAsync<GetProgressFormResponse>($"/api/progress-forms/{theirs.Id}", Ct))!.Title.Should().Be("Mood scale");
    }
}
