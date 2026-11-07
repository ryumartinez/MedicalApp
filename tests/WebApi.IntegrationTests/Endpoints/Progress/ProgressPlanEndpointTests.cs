using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Persistence;
using MedicalApp.Features.Access.CreatePlanGrant;
using MedicalApp.Features.Access.CreateTreatment;
using MedicalApp.Features.Access.RevokeTreatment;
using MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;
using MedicalApp.Features.ProgressPlans;
using MedicalApp.Features.ProgressPlans.AssignProgressPlan;
using MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;
using MedicalApp.Features.ProgressPlans.GetProgressPlan;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static MedicalApp.IntegrationTests.Endpoints.Progress.ProgressApi;

namespace MedicalApp.IntegrationTests.Endpoints.Progress;

[Collection<ProgressApiCollection>]
public sealed class ProgressPlanEndpointTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private ProgressScenario _s = null!;

    public async ValueTask InitializeAsync() => _s = await ProgressScenario.CreateAsync(fixture);

    public ValueTask DisposeAsync()
    {
        _s.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact]
    public async Task AssignsDailyAndWeeklyPlansThroughTreatmentWithoutAppointments()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();

        var daily = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId);
        var weekly = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId, ScheduleFrequency.Weekly,
            "America/Santiago", new DateOnly(2026, 11, 4), new DateOnly(2026, 12, 31));

        using (var scope = fixture.CreateScope())
            (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<Appointment>().CountAsync(Ct)).Should().Be(0);
        weekly.Frequency.Should().Be(ScheduleFrequency.Weekly);
        weekly.TimeZone.Should().Be("America/Santiago");
        weekly.EndDate.Should().Be(new DateOnly(2026, 12, 31));
        daily.DoctorId.Should().Be(_s.DoctorA.Id.Value);

        var patientView = await _s.PatientAClient.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{weekly.Id}", Ct);
        patientView!.Form.Id.Should().Be(version.VersionId);
        patientView.Form.Questions.Should().HaveCount(5);
        var mine = await _s.PatientAClient.GetFromJsonAsync<GetPatientProgressPlansResponse>("/api/progress-plans", Ct);
        mine!.Items.Select(p => p.Id).Should().BeEquivalentTo([daily.Id, weekly.Id]);

        using var otherPatient = await _s.PatientBClient.GetAsync($"/api/progress-plans/{weekly.Id}", Ct);
        using var otherDoctor = await _s.DoctorBClient.GetAsync($"/api/progress-plans/{weekly.Id}", Ct);
        otherPatient.StatusCode.Should().Be(HttpStatusCode.NotFound);
        otherDoctor.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await _s.PatientBClient.GetFromJsonAsync<GetPatientProgressPlansResponse>($"/api/progress-plans?patientId={_s.PatientA.Id.Value}", Ct))!
            .Items.Should().BeEmpty();
        (await _s.DoctorBClient.GetFromJsonAsync<GetPatientProgressPlansResponse>($"/api/progress-plans?patientId={_s.PatientA.Id.Value}", Ct))!
            .Items.Should().BeEmpty();
        using var noPatient = await _s.DoctorAClient.GetAsync("/api/progress-plans", Ct);
        noPatient.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task RejectsInvalidAssignments()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();
        var otherVersion = await _s.DoctorBClient.CreatePublishedFormAsync();
        var draftOnly = await _s.DoctorAClient.CreateFormAsync();

        // An appointment without treatment grants nothing.
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.Add(Appointment.Create(_s.PatientB.Id, _s.DoctorA.Id, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), null));
            await db.SaveChangesAsync(Ct);
        }

        async Task<HttpStatusCode> Assign(Guid patient, Guid versionId, string zone = "UTC", DateOnly? end = null)
        {
            using var response = await _s.DoctorAClient.PostAsJsonAsync("/api/progress-plans", new AssignProgressPlanRequest(
                patient, versionId, ScheduleFrequency.Daily, zone, new DateOnly(2026, 11, 1), end, null), Ct);
            return response.StatusCode;
        }

        (await Assign(_s.PatientB.Id.Value, version.VersionId)).Should().Be(HttpStatusCode.NotFound);
        (await Assign(_s.PatientA.Id.Value, otherVersion.VersionId)).Should().Be(HttpStatusCode.NotFound);
        (await Assign(_s.PatientA.Id.Value, draftOnly.Id)).Should().Be(HttpStatusCode.NotFound);
        (await Assign(_s.PatientA.Id.Value, version.VersionId, "Mars/Base")).Should().Be(HttpStatusCode.BadRequest);
        (await Assign(_s.PatientA.Id.Value, version.VersionId, end: new DateOnly(2026, 10, 1))).Should().Be(HttpStatusCode.BadRequest);
        using (var patientAssign = await _s.PatientAClient.PostAsJsonAsync("/api/progress-plans", new AssignProgressPlanRequest(
                   _s.PatientA.Id.Value, version.VersionId, ScheduleFrequency.Daily, "UTC", new DateOnly(2026, 11, 1), null, null), Ct))
            patientAssign.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var scope2 = fixture.CreateScope();
        (await scope2.ServiceProvider.GetRequiredService<ApplicationDbContext>().ProgressPlans.CountAsync(Ct)).Should().Be(0);
    }

    [Fact]
    public async Task ReplacementEndsThePreviousPlanAndKeepsItsVersion()
    {
        var created = await _s.DoctorAClient.CreateFormAsync();
        var v1 = await _s.DoctorAClient.PublishAsync(created.Id, created.Revision);
        var original = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, v1.VersionId);
        using (var update = await _s.DoctorAClient.PutAsJsonAsync($"/api/progress-forms/{created.Id}/draft",
                   new UpdateProgressFormDraftRequest(created.Id, v1.Revision, "Changed", null, AllTypes().Take(1).ToList()), Ct))
        {
            var updated = await update.ReadAsync<UpdateProgressFormDraftResponse>();
            var v2 = await _s.DoctorAClient.PublishAsync(created.Id, updated.Revision);
            var replacement = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, v2.VersionId, ScheduleFrequency.Weekly, replaces: original.Id);
            replacement.FormVersionId.Should().Be(v2.VersionId);
        }

        var old = await _s.DoctorAClient.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{original.Id}", Ct);
        old!.Plan.EndReason.Should().Be(PlanEndReason.Replaced);
        old.Plan.ReplacedByPlanId.Should().NotBeNull();
        old.Form.VersionNumber.Should().Be(1);
        old.Form.Questions.Should().HaveCount(5);
        (await _s.DoctorAClient.GetFromJsonAsync<GetPatientProgressPlansResponse>($"/api/progress-plans?patientId={_s.PatientA.Id.Value}&includeEnded=true", Ct))!
            .Items.Should().HaveCount(2);

        using var again = await _s.DoctorAClient.PostAsJsonAsync("/api/progress-plans", new AssignProgressPlanRequest(
            _s.PatientA.Id.Value, v1.VersionId, ScheduleFrequency.Daily, "UTC", new DateOnly(2026, 11, 1), null, original.Id), Ct);
        again.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task EndingAPlanRequiresTheCurrentRevision()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId);

        using var stale = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { Revision = plan.Revision + 1 }, Ct);
        using var other = await _s.DoctorBClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { plan.Revision }, Ct);
        using var patient = await _s.PatientAClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { plan.Revision }, Ct);
        using var ended = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { plan.Revision }, Ct);
        var endedPlan = await ended.ReadAsync<PlanDto>();
        using var twice = await _s.DoctorAClient.PostAsJsonAsync($"/api/progress-plans/{plan.Id}/end", new { endedPlan.Revision }, Ct);

        stale.StatusCode.Should().Be(HttpStatusCode.Conflict);
        other.StatusCode.Should().Be(HttpStatusCode.NotFound);
        patient.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        endedPlan.EndReason.Should().Be(PlanEndReason.EndedByDoctor);
        twice.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task ReviewRequiresBothGrantAndTreatment()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId);

        using var grantResponse = await _s.Administrator.PostAsJsonAsync("/api/access/plan-grants",
            new CreatePlanGrantRequest(plan.Id, _s.DoctorB.Id.Value), Ct);
        var grant = await grantResponse.ReadAsync<CreatePlanGrantResponse>();
        (await StatusAsync(_s.DoctorBClient, plan.Id)).Should().Be(HttpStatusCode.NotFound, "a grant does not replace treatment");

        using var treatResponse = await _s.Administrator.PostAsJsonAsync("/api/access/treatments",
            new CreateTreatmentRequest(_s.DoctorB.Id.Value, _s.PatientA.Id.Value), Ct);
        var treatment = await treatResponse.ReadAsync<CreateTreatmentResponse>();
        (await StatusAsync(_s.DoctorBClient, plan.Id)).Should().Be(HttpStatusCode.OK);
        (await _s.DoctorBClient.GetFromJsonAsync<GetPatientProgressPlansResponse>($"/api/progress-plans?patientId={_s.PatientA.Id.Value}", Ct))!
            .Items.Select(p => p.Id).Should().Equal(plan.Id);

        // Revoking the reviewer's treatment ends their access but leaves the owner's plan open.
        using (var revoke = await _s.Administrator.PostAsync($"/api/access/treatments/{treatment.Id}/revoke", null, Ct))
            (await revoke.ReadAsync<RevokeTreatmentResponse>()).ClosedPlanIds.Should().BeEmpty();
        (await StatusAsync(_s.DoctorBClient, plan.Id)).Should().Be(HttpStatusCode.NotFound);
        (await _s.DoctorAClient.GetFromJsonAsync<GetProgressPlanResponse>($"/api/progress-plans/{plan.Id}", Ct))!.Plan.EndedAt.Should().BeNull();

        using (await _s.Administrator.PostAsJsonAsync("/api/access/treatments", new CreateTreatmentRequest(_s.DoctorB.Id.Value, _s.PatientA.Id.Value), Ct)) { }
        (await StatusAsync(_s.DoctorBClient, plan.Id)).Should().Be(HttpStatusCode.OK);
        using (await _s.Administrator.PostAsync($"/api/access/plan-grants/{grant.Id}/revoke", null, Ct)) { }
        (await StatusAsync(_s.DoctorBClient, plan.Id)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RevocationClosesOnlyTheAffectedDoctorsPlans()
    {
        await _s.AddTreatmentAsync(_s.DoctorB.Id, _s.PatientA.Id);
        var aVersion = await _s.DoctorAClient.CreatePublishedFormAsync();
        var bVersion = await _s.DoctorBClient.CreatePublishedFormAsync();
        var aPlans = new[]
        {
            await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, aVersion.VersionId),
            await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, aVersion.VersionId, ScheduleFrequency.Weekly)
        };
        var bPlan = await _s.DoctorBClient.AssignAsync(_s.PatientA.Id.Value, bVersion.VersionId);
        Guid treatmentId;
        using (var scope = fixture.CreateScope())
            treatmentId = (await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().DoctorPatientRelationships
                .SingleAsync(r => r.DoctorId == _s.DoctorA.Id && r.PatientId == _s.PatientA.Id, Ct)).Id.Value;

        using var revoke = await _s.Administrator.PostAsync($"/api/access/treatments/{treatmentId}/revoke", null, Ct);
        var result = await revoke.ReadAsync<RevokeTreatmentResponse>();

        result.ClosedPlanIds.Should().BeEquivalentTo(aPlans.Select(p => p.Id));
        var patientView = await _s.PatientAClient.GetFromJsonAsync<GetPatientProgressPlansResponse>("/api/progress-plans?includeEnded=true", Ct);
        patientView!.Items.Where(p => p.DoctorId == _s.DoctorA.Id.Value).Should().OnlyContain(p => p.EndReason == PlanEndReason.TreatmentRevoked);
        patientView.Items.Single(p => p.Id == bPlan.Id).EndedAt.Should().BeNull();
        (await StatusAsync(_s.DoctorAClient, aPlans[0].Id)).Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OnlyAdministratorsManageAccess()
    {
        var version = await _s.DoctorAClient.CreatePublishedFormAsync();
        var plan = await _s.DoctorAClient.AssignAsync(_s.PatientA.Id.Value, version.VersionId);

        using var treat = await _s.DoctorBClient.PostAsJsonAsync("/api/access/treatments", new CreateTreatmentRequest(_s.DoctorB.Id.Value, _s.PatientA.Id.Value), Ct);
        using var grant = await _s.DoctorBClient.PostAsJsonAsync("/api/access/plan-grants", new CreatePlanGrantRequest(plan.Id, _s.DoctorB.Id.Value), Ct);
        using var patient = await _s.PatientAClient.PostAsJsonAsync("/api/access/treatments", new CreateTreatmentRequest(_s.DoctorB.Id.Value, _s.PatientA.Id.Value), Ct);
        using var anonymous = await fixture.CreateAnonymousClient().PostAsJsonAsync("/api/access/treatments", new CreateTreatmentRequest(_s.DoctorB.Id.Value, _s.PatientA.Id.Value), Ct);
        using var ownerGrant = await _s.Administrator.PostAsJsonAsync("/api/access/plan-grants", new CreatePlanGrantRequest(plan.Id, _s.DoctorA.Id.Value), Ct);

        treat.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        grant.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        patient.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ownerGrant.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        (await db.DoctorPatientRelationships.CountAsync(Ct)).Should().Be(1);
        (await db.ProgressPlanGrants.CountAsync(Ct)).Should().Be(0);
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, Guid planId)
    {
        using var response = await client.GetAsync($"/api/progress-plans/{planId}", Ct);
        return response.StatusCode;
    }
}
