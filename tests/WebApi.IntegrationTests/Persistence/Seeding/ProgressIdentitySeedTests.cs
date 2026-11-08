using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationService.Initializers;

namespace MedicalApp.IntegrationTests.Persistence.Seeding;

public sealed class ProgressIdentitySeedTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Issuer = MigrationServices.DevelopmentIssuer;
    private const string KeycloakIssuer = "http://localhost:8080/realms/medicalapp";

    [Fact]
    public async Task SeedsMatchingDevelopmentMappingsOnce()
    {
        await RunWorkerAsync(Environments.Development);
        await RunWorkerAsync(Environments.Development);

        var identities = await GetQueryable<ApplicationIdentity>().OrderBy(x => x.Subject).ToListAsync(CancellationToken);
        identities.Should().HaveCount(2).And.OnlyContain(x => x.Enabled && x.Issuer == Issuer);
        identities[0].DoctorId.Should().Be(ProgressIdentityInitializer.DoctorId);
        identities[1].PatientId.Should().Be(ProgressIdentityInitializer.PatientId);
        (await GetQueryable<Doctor>().CountAsync(x => x.Id == ProgressIdentityInitializer.DoctorId, CancellationToken)).Should().Be(1);
        (await GetQueryable<Patient>().CountAsync(x => x.Id == ProgressIdentityInitializer.PatientId, CancellationToken)).Should().Be(1);
        (await GetQueryable<DoctorPatientRelationship>().SingleAsync(CancellationToken)).IsActive.Should().BeTrue();

        // Subjects must match the seeded development accounts.
        var users = await GetQueryable<ApplicationUser>().ToDictionaryAsync(x => x.UserName!, x => x.Subject, CancellationToken);
        identities[0].Subject.Should().Be(users["development-doctor"]);
        identities[1].Subject.Should().Be(users["development-patient"]);
    }

    [Fact]
    public async Task MovesSeededMappingsFromTheKeycloakIssuer()
    {
        await SeedWithKeycloakIssuerAsync();

        await RunWorkerAsync(Environments.Development);

        var identities = await GetQueryable<ApplicationIdentity>().OrderBy(x => x.Subject).ToListAsync(CancellationToken);
        identities.Should().HaveCount(2).And.OnlyContain(x => x.Enabled && x.Issuer == Issuer);
        identities[0].DoctorId.Should().Be(ProgressIdentityInitializer.DoctorId);
        identities[1].PatientId.Should().Be(ProgressIdentityInitializer.PatientId);
    }

    [Fact]
    public async Task MovesADisabledSeededMappingAndKeepsItDisabled()
    {
        await SeedWithKeycloakIssuerAsync();
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var identity = await db.ApplicationIdentities.SingleAsync(x => x.Id == ProgressIdentityInitializer.PatientIdentityId, CancellationToken);
            identity.Disable();
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunWorkerAsync(Environments.Development);
        var moved = await GetQueryable<ApplicationIdentity>().SingleAsync(x => x.Id == ProgressIdentityInitializer.PatientIdentityId, CancellationToken);
        await RunWorkerAsync(Environments.Development);
        var repeated = await GetQueryable<ApplicationIdentity>().SingleAsync(x => x.Id == ProgressIdentityInitializer.PatientIdentityId, CancellationToken);

        moved.Issuer.Should().Be(Issuer);
        moved.Enabled.Should().BeFalse();
        moved.PatientId.Should().Be(ProgressIdentityInitializer.PatientId);
        repeated.Should().BeEquivalentTo(moved);
    }

    [Fact]
    public async Task PreservesExistingMappingState()
    {
        await RunWorkerAsync(Environments.Development);
        using (var scope = CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var identity = await db.ApplicationIdentities.SingleAsync(x => x.Id == ProgressIdentityInitializer.DoctorIdentityId, CancellationToken);
            identity.Disable();
            await db.SaveChangesAsync(CancellationToken);
        }

        await RunWorkerAsync(Environments.Development);

        (await GetQueryable<ApplicationIdentity>().SingleAsync(x => x.Id == ProgressIdentityInitializer.DoctorIdentityId, CancellationToken))
            .Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task SeedsTheThreeClinicalWorkflowsOnce()
    {
        await RunWorkerAsync(Environments.Development);
        await RunWorkerAsync(Environments.Development);

        var plans = await GetQueryable<ProgressPlan>().OrderBy(p => p.FormId).ToListAsync(CancellationToken);
        plans.Select(p => p.Frequency).Should().Equal(ScheduleFrequency.Daily, ScheduleFrequency.Weekly, ScheduleFrequency.Weekly);
        plans.Should().OnlyContain(p => p.PatientId == ProgressIdentityInitializer.PatientId && p.DoctorId == ProgressIdentityInitializer.DoctorId && p.IsOpen);
        (await GetQueryable<ProgressForm>().CountAsync(CancellationToken)).Should().Be(3);
        (await GetQueryable<ProgressFormVersion>().CountAsync(CancellationToken)).Should().Be(3);
        var versions = await GetQueryable<ProgressFormVersion>().ToListAsync(CancellationToken);
        versions.Single(v => v.FormId == ProgressWorkflowInitializer.ExerciseVideoFormId).Questions.Should().OnlyContain(q => q.Type == QuestionType.Video);
        versions.Single(v => v.FormId == ProgressWorkflowInitializer.KneeTherapyFormId).Questions.Select(q => q.Type).Should().Contain(QuestionType.Image);

        var entries = await GetQueryable<ProgressEntry>().ToListAsync(CancellationToken);
        entries.Should().HaveCount(5).And.OnlyContain(e => e.IsSubmitted && e.PlanId == plans[0].Id);
        entries.Count(e => e.IsLate).Should().Be(1);
        foreach (var entry in entries)
            ProgressEntry.ValidateAnswers(versions.Single(v => v.Id == entry.FormVersionId), entry.Answers).Should().BeEmpty();
    }

    [Fact]
    public async Task SkipsSeedOutsideDevelopment()
    {
        await RunWorkerAsync(Environments.Production);

        (await GetQueryable<ApplicationIdentity>().CountAsync(CancellationToken)).Should().Be(0);
        (await GetQueryable<Doctor>().CountAsync(CancellationToken)).Should().Be(0);
        (await GetQueryable<ProgressPlan>().CountAsync(CancellationToken)).Should().Be(0);
    }

    /// <summary>
    /// Leaves the seeded mappings as the Keycloak-era seed stored them: the realm URL without a trailing slash.
    /// </summary>
    private async Task SeedWithKeycloakIssuerAsync()
    {
        await RunWorkerAsync(Environments.Development);
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.ApplicationIdentities.ExecuteUpdateAsync(update => update.SetProperty(x => x.Issuer, KeycloakIssuer), CancellationToken);
        (await GetQueryable<ApplicationIdentity>().ToListAsync(CancellationToken)).Should().HaveCount(2).And.OnlyContain(x => x.Issuer == KeycloakIssuer);
    }

    private Task RunWorkerAsync(string environment, IDictionary<string, string?>? settings = null)
    {
        string connectionString;
        using (var scope = CreateScope())
            connectionString = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;

        return MigrationServices.RunWorkerAsync(connectionString, environment, settings, CancellationToken);
    }
}
