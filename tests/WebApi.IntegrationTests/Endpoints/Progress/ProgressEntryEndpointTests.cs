using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Persistence;
using MedicalApp.Features.ProgressEntries;
using MedicalApp.Features.ProgressEntries.GetDueProgressEntries;
using MedicalApp.Features.ProgressPlans;
using MedicalApp.Features.ProgressPlans.GetProgressPlan;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

[Collection<ProgressApiCollection>]
public sealed class ProgressEntryEndpointTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;
    private PlanDto _plan = null!;
    private GetProgressPlanResponse.FormVersionDto _version = null!;

    public async ValueTask InitializeAsync()
    {
        _s = await ProgressScenario.CreateAsync(fixture);
        var published = await _s.DoctorAClient.CreatePublishedFormAsync();
        _plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, published.VersionId, start: Today.AddDays(-3));
        _version = await _s.PatientAClient.GetVersionAsync(_plan.Id);
    }

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ListsDueAndMissedPeriodsFromTheSchedule()
    {
        var due = await _s.PatientAClient.GetFromJsonAsync<GetDueProgressEntriesResponse>("/api/progress-entries/due", Ct);

        due!.Items.Select(i => i.PeriodStartDate).Should().Equal(Today.AddDays(-3), Today.AddDays(-2), Today.AddDays(-1), Today);
        due.Items.Select(i => i.State).Should().Equal(PeriodState.Missed, PeriodState.Missed, PeriodState.Missed, PeriodState.Due);
        due.Items.Should().OnlyContain(i => i.EntryId == null);
        (await _s.PatientBClient.GetFromJsonAsync<GetDueProgressEntriesResponse>($"/api/progress-entries/due?planId={_plan.Id}", Ct))!
            .Items.Should().BeEmpty();

        using var future = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today.AddDays(1), null);
        future.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await future.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("PeriodNotStarted");
        using var scope = fixture.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProgressEntries.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task SavesPartialDraftsAndDetectsConflicts()
    {
        using var created = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, new AnswerDto(_version.Q("mood"), Number: 4));
        var draft = await created.ReadAsync<EntryDto>();
        draft.Status.Should().Be(EntryStatus.Draft);
        draft.PatientId.Should().Be(_s.PatientA.Id.Value);

        using var duplicateCreate = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, new AnswerDto(_version.Q("mood"), Number: 6));
        using var updated = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, draft.Revision, new AnswerDto(_version.Q("mood"), Number: 8));
        using var stale = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, draft.Revision, new AnswerDto(_version.Q("mood"), Number: 2));

        duplicateCreate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        updated.StatusCode.Should().Be(HttpStatusCode.OK);
        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var saved = await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct);
        saved!.Answers.Single().Number.Should().Be(8);
    }

    [Fact]
    public async Task RejectsInvalidAnswersAgainstTheAssignedVersion()
    {
        var sleep = _version.Questions.Single(q => q.Key == "sleep");
        var cases = new[]
        {
            new[] { new AnswerDto(Guid.CreateVersion7(), Text: "unknown question") },
            new[] { new AnswerDto(_version.Q("mood"), Number: 3), new AnswerDto(_version.Q("mood"), Number: 4) },
            new[] { new AnswerDto(sleep.Id!.Value, OptionId: Guid.CreateVersion7()) },
            new[] { new AnswerDto(_version.Q("mood"), Number: 11) },
            new[] { new AnswerDto(_version.Q("mood"), Number: 2.5m) },
            new[] { new AnswerDto(_version.Q("notes"), Text: "two", Number: 2) },
            new[] { new AnswerDto(_version.Q("notes"), Number: 2) },
            new[] { new AnswerDto(_version.Q("notes"), Text: " ") },
            new[] { new AnswerDto(_version.Q("notes"), Text: new string('a', 4001)) },
            new[] { new AnswerDto(_version.Q("photo"), MediaAssetId: Guid.CreateVersion7()) }
        };

        foreach (var answers in cases)
        {
            using var response = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, answers);
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "answers {0} are invalid", System.Text.Json.JsonSerializer.Serialize(answers));
        }

        using var valid = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null,
            new AnswerDto(sleep.Id!.Value, OptionId: sleep.Options![1].Id), new AnswerDto(_version.Q("notes"), Text: new string('a', 4000)));
        valid.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ClientsCannotActForAnotherPatient()
    {
        // Extra identity and timestamp fields are ignored; the mapping decides the acting patient.
        using var spoofed = await _s.PatientBClient.PutAsJsonAsync("/api/progress-entries/draft", new
        {
            PlanId = _plan.Id, PeriodStartDate = Today, PatientId = _s.PatientA.Id.Value, Answers = Array.Empty<object>()
        }, Ct);
        using var doctor = await _s.DoctorAClient.SaveDraftAsync(_plan.Id, Today, null);
        using var own = await _s.PatientAClient.PutAsJsonAsync("/api/progress-entries/draft", new
        {
            PlanId = _plan.Id, PeriodStartDate = Today, PatientId = _s.PatientB.Id.Value, SubmittedAt = DateTimeOffset.UnixEpoch,
            Answers = RequiredAnswers(_version)
        }, Ct);
        var entry = await own.ReadAsync<EntryDto>();
        using var otherSubmit = await _s.PatientBClient.SubmitAsync(entry.Id);
        using var otherRead = await _s.PatientBClient.GetAsync($"/api/progress-entries/{entry.Id}", Ct);
        using var otherDoctorRead = await _s.DoctorBClient.GetAsync($"/api/progress-entries/{entry.Id}", Ct);
        using var anonymous = await fixture.CreateAnonymousClient().GetAsync($"/api/progress-entries/{entry.Id}", Ct);

        spoofed.StatusCode.Should().Be(HttpStatusCode.NotFound);
        doctor.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        entry.PatientId.Should().Be(_s.PatientA.Id.Value);
        entry.SubmittedAt.Should().BeNull();
        otherSubmit.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherDoctorRead.StatusCode.Should().Be(HttpStatusCode.NotFound);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await _s.DoctorAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{entry.Id}", Ct))!.Id.Should().Be(entry.Id);
    }

    [Fact]
    public async Task SubmitsOnceAndRetriesWithoutWriting()
    {
        using var partial = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, new AnswerDto(_version.Q("mood"), Number: 4));
        var draft = await partial.ReadAsync<EntryDto>();
        using var incomplete = await _s.PatientAClient.SubmitAsync(draft.Id);
        incomplete.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await incomplete.Content.ReadAsStringAsync(Ct)).Should().ContainEquivalentOf("RequiredMissing");

        using var completed = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, draft.Revision, RequiredAnswers(_version));
        var ready = await completed.ReadAsync<EntryDto>();
        var before = DateTimeOffset.UtcNow;
        using var submittedResponse = await _s.PatientAClient.SubmitAsync(ready.Id, ready.Revision);
        var submitted = await submittedResponse.ReadAsync<EntryDto>();

        submitted.Status.Should().Be(EntryStatus.Submitted);
        submitted.IsLate.Should().BeFalse();
        submitted.SubmittedAt.Should().BeOnOrAfter(before.AddSeconds(-1)).And.BeOnOrBefore(DateTimeOffset.UtcNow.AddSeconds(1));

        using var retry = await _s.PatientAClient.SubmitAsync(ready.Id, ready.Revision);
        var retried = await retry.ReadAsync<EntryDto>();
        retried.Should().BeEquivalentTo(submitted);
        retried.Revision.Should().Be(submitted.Revision, "a retry writes nothing");

        using var change = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, submitted.Revision, new AnswerDto(_version.Q("mood"), Number: 1));
        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{ready.Id}", Ct))!.Answers.Should().BeEquivalentTo(ready.Answers);
        (await _s.PatientAClient.GetFromJsonAsync<GetDueProgressEntriesResponse>("/api/progress-entries/due", Ct))!
            .Items.Should().NotContain(i => i.PeriodStartDate == Today);
    }

    [Fact]
    public async Task LateSubmissionsKeepTheirPeriod()
    {
        var period = Today.AddDays(-2);
        using var saved = await _s.PatientAClient.SaveDraftAsync(_plan.Id, period, null, RequiredAnswers(_version));
        var draft = await saved.ReadAsync<EntryDto>();

        using var submittedResponse = await _s.PatientAClient.SubmitAsync(draft.Id);
        var submitted = await submittedResponse.ReadAsync<EntryDto>();

        submitted.IsLate.Should().BeTrue();
        submitted.PeriodStartDate.Should().Be(period);
        submitted.PeriodEndUtc.Should().BeBefore(submitted.SubmittedAt!.Value);
    }

    [Fact]
    public async Task ConcurrentRequestsKeepOneEntryAndOneSubmission()
    {
        var creations = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, RequiredAnswers(_version))));
        creations.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        creations.Where(r => r.StatusCode != HttpStatusCode.OK).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);
        var entry = await creations.Single(r => r.IsSuccessStatusCode).ReadAsync<EntryDto>();

        var submissions = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => _s.PatientAClient.SubmitAsync(entry.Id)));
        var results = await Task.WhenAll(submissions.Select(r => r.ReadAsync<EntryDto>()));

        results.Select(r => r.SubmittedAt).Distinct().Should().ContainSingle();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.ProgressEntries.CountAsync(e => e.PlanId == ProgressPlanId.From(_plan.Id), Ct)).Should().Be(1);
        foreach (var response in creations.Concat(submissions))
            response.Dispose();
    }

    [Fact]
    public async Task ClosedPlansKeepEntriesAndRejectChanges()
    {
        using var submittedDraft = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today.AddDays(-1), null, RequiredAnswers(_version));
        var submitted = await (await _s.PatientAClient.SubmitAsync((await submittedDraft.ReadAsync<EntryDto>()).Id)).ReadAsync<EntryDto>();
        using var openDraft = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, RequiredAnswers(_version));
        var draft = await openDraft.ReadAsync<EntryDto>();
        var plan = await _s.DoctorAClient.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{_plan.Id}", Ct);

        using (var end = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{_plan.Id}/end", new { plan!.Plan.Revision }, Ct))
            end.StatusCode.Should().Be(HttpStatusCode.OK);

        using var change = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, draft.Revision, RequiredAnswers(_version));
        using var create = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today.AddDays(-2), null, RequiredAnswers(_version));
        using var submit = await _s.PatientAClient.SubmitAsync(draft.Id);
        change.StatusCode.Should().Be(HttpStatusCode.Conflict);
        create.StatusCode.Should().Be(HttpStatusCode.Conflict);
        submit.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{submitted.Id}", Ct))!.Status.Should().Be(EntryStatus.Submitted);
        (await _s.DoctorAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct))!.Status.Should().Be(EntryStatus.Draft);
        (await _s.PatientAClient.GetFromJsonAsync<GetDueProgressEntriesResponse>("/api/progress-entries/due", Ct))!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task ClosureAndSubmissionCommitInAConsistentOrder()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();
        for (var round = 0; round < 6; round++)
        {
            var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId);
            using var saved = await _s.PatientAClient.SaveDraftAsync(plan.Id, Today, null, RequiredAnswers(_version with { Questions = (await _s.PatientAClient.GetVersionAsync(plan.Id)).Questions }));
            var draft = await saved.ReadAsync<EntryDto>();

            var end = _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { plan.Revision }, Ct);
            var submit = _s.PatientAClient.SubmitAsync(draft.Id);
            using var endResponse = await end;
            using var submitResponse = await submit;

            endResponse.StatusCode.Should().Be(HttpStatusCode.OK);
            var ended = await endResponse.ReadAsync<PlanDto>();
            var entry = await _s.DoctorAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct);
            if (submitResponse.StatusCode == HttpStatusCode.OK)
                entry!.SubmittedAt.Should().BeBefore(ended.EndedAt!.Value, "a submission cannot commit after closure");
            else
            {
                submitResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
                entry!.Status.Should().Be(EntryStatus.Draft);
            }
        }
    }

    [Fact]
    public async Task RevocationAndSubmissionCommitInAConsistentOrder()
    {
        Guid treatmentId;
        using (var scope = fixture.CreateScope())
            treatmentId = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DoctorPatientRelationships.SingleAsync(Ct)).Id.Value;
        using var saved = await _s.PatientAClient.SaveDraftAsync(_plan.Id, Today, null, RequiredAnswers(_version));
        var draft = await saved.ReadAsync<EntryDto>();

        var revoke = _s.Administrator.PostAsync($"/api/access/treatments/{treatmentId}/revoke", null, Ct);
        var submit = _s.PatientAClient.SubmitAsync(draft.Id);
        using var revokeResponse = await revoke;
        using var submitResponse = await submit;

        revokeResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var plan = (await _s.PatientAClient.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{_plan.Id}", Ct))!.Plan;
        plan.EndReason.Should().Be(PlanEndReason.TreatmentRevoked);
        var entry = await _s.PatientAClient.GetFromJsonAsync<EntryDto>($"/api/progress-entries/{draft.Id}", Ct);
        if (submitResponse.StatusCode == HttpStatusCode.OK)
            entry!.SubmittedAt.Should().BeBefore(plan.EndedAt!.Value);
        else
            entry!.Status.Should().Be(EntryStatus.Draft);
        entry.Answers.Should().NotBeEmpty("closure preserves entries");
    }
}
