using System.Text.Json.Nodes;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Persistence;
using MedicalApp.Common.Persistence.Interceptors;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using MigrationService;
using MigrationService.Initializers;
using NSubstitute;

namespace MedicalApp.IntegrationTests.Persistence.Seeding;

public sealed class ProgressIdentitySeedTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Issuer = "http://localhost:8080/realms/medicalapp";

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

        // Subjects must match the development accounts that Keycloak imports.
        var realm = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "medicalapp-realm.json"), CancellationToken))!;
        var users = realm["users"]!.AsArray().ToDictionary(x => x!["username"]!.GetValue<string>(), x => x!["id"]!.GetValue<string>());
        identities[0].Subject.Should().Be(users["development-doctor"]);
        identities[1].Subject.Should().Be(users["development-patient"]);
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

    private async Task RunWorkerAsync(string environment)
    {
        string connectionString;
        using (var scope = CreateScope())
            connectionString = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [ProgressIdentityInitializer.IssuerKey] = Issuer })
            .Build());
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment });
        services.AddSingleton<ICurrentUserService, MigrationUserService>();
        services.AddSingleton(TimeProvider.System);
        services.AddDbContext<ApplicationDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(new EntitySaveChangesInterceptor(provider.GetRequiredService<ICurrentUserService>(), TimeProvider.System)));
        services.AddScoped<ApplicationDbContextInitializer>();
        services.AddScoped<ProgressIdentityInitializer>();
        services.AddScoped<ProgressWorkflowInitializer>();
        await using var provider = services.BuildServiceProvider();

        var worker = new Worker(provider, Substitute.For<IHostApplicationLifetime>(), provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<Worker>>());
        await worker.StartAsync(CancellationToken);
        await worker.ExecuteTask!;
    }
}
