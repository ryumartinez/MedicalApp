using System.Security.Claims;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Persistence;
using MedicalApp.Common.Services;
using MedicalApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Authorization;

public sealed class CurrentIdentityResolutionTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Issuer = "https://identity.example/realms/medicalapp";

    [Fact]
    public async Task ResolvesEnabledDualRoleMappingAndAuditIdentifier()
    {
        var (doctor, patient) = await AddPeopleAsync();
        var identity = ApplicationIdentity.Create(Issuer, "dual");
        identity.LinkDoctor(doctor.Id);
        identity.LinkPatient(patient.Id);
        identity.Enable();
        await AddAsync(identity);

        var (current, context) = await ResolveAsync("dual", ApiRoles.Doctor, ApiRoles.Patient);

        current!.ApplicationIdentityId.Should().Be(identity.Id);
        current.DoctorId.Should().Be(doctor.Id);
        current.PatientId.Should().Be(patient.Id);
        new CurrentUserService(new HttpContextAccessor { HttpContext = context }).UserId.Should().Be(identity.Id.Value.ToString());
    }

    [Fact]
    public async Task RequiresRoleForEachMapping()
    {
        var (doctor, patient) = await AddPeopleAsync();
        var identity = ApplicationIdentity.Create(Issuer, "patient-role-only");
        identity.LinkDoctor(doctor.Id);
        identity.LinkPatient(patient.Id);
        identity.Enable();
        await AddAsync(identity);

        var (current, _) = await ResolveAsync("patient-role-only", ApiRoles.Patient);

        current!.DoctorId.Should().BeNull();
        current.PatientId.Should().Be(patient.Id);
    }

    [Fact]
    public async Task DisabledMappingGrantsNoRecords()
    {
        var (doctor, _) = await AddPeopleAsync();
        var identity = ApplicationIdentity.Create(Issuer, "disabled");
        identity.LinkDoctor(doctor.Id);
        await AddAsync(identity);

        var (current, _) = await ResolveAsync("disabled", ApiRoles.Doctor);

        current!.ApplicationIdentityId.Should().Be(identity.Id);
        current.DoctorId.Should().BeNull();
        current.PatientId.Should().BeNull();
    }

    [Fact]
    public async Task UnmappedAndMigrationIdentitiesGrantNoRecords()
    {
        await AddPeopleAsync();

        var (unmapped, unmappedContext) = await ResolveAsync("unmapped", ApiRoles.Doctor, ApiRoles.Patient);
        var (migration, _) = await ResolveAsync("MigrationService", ApiRoles.Doctor);

        unmapped!.ApplicationIdentityId.Should().BeNull();
        unmapped.DoctorId.Should().BeNull();
        migration!.DoctorId.Should().BeNull();
        new CurrentUserService(new HttpContextAccessor { HttpContext = unmappedContext }).UserId.Should().Be($"unmapped@{Issuer}");
    }

    private async Task<(Doctor Doctor, Patient Patient)> AddPeopleAsync()
    {
        var doctor = Doctor.Create("Dr. Smith", "Psychology");
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        await AddAsync(doctor);
        await AddAsync(patient);
        return (doctor, patient);
    }

    private async Task<(MedicalApp.Common.Interfaces.CurrentIdentity? Identity, HttpContext Context)> ResolveAsync(string subject, params string[] roles)
    {
        var claims = new List<Claim> { new("iss", Issuer), new("sub", subject) };
        claims.AddRange(roles.Select(role => new Claim(ApiRoles.ClaimType, role)));
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test")) };
        using var scope = CreateScope();
        var service = new CurrentIdentityService(new HttpContextAccessor { HttpContext = context },
            scope.ServiceProvider.GetRequiredService<ApplicationDbContext>());
        return (await service.GetAsync(CancellationToken), context);
    }
}
