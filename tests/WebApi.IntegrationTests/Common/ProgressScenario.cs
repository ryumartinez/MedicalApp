using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Common;

/// <summary>
/// Two doctors and two patients with real Keycloak accounts and enabled mappings.
/// Doctor A treats patient A. Other relationships are added by each test.
/// </summary>
public sealed class ProgressScenario : IDisposable
{
    private readonly ProgressApiFixture _fixture;
    private readonly List<HttpClient> _clients = [];

    public Doctor DoctorA { get; private set; } = null!;
    public Doctor DoctorB { get; private set; } = null!;
    public Patient PatientA { get; private set; } = null!;
    public Patient PatientB { get; private set; } = null!;
    public HttpClient DoctorAClient { get; private set; } = null!;
    public HttpClient DoctorBClient { get; private set; } = null!;
    public HttpClient PatientAClient { get; private set; } = null!;
    public HttpClient PatientBClient { get; private set; } = null!;
    public HttpClient Administrator { get; private set; } = null!;

    private ProgressScenario(ProgressApiFixture fixture) => _fixture = fixture;

    public static async Task<ProgressScenario> CreateAsync(ProgressApiFixture fixture)
    {
        await fixture.ResetAsync();
        var scenario = new ProgressScenario(fixture);
        await scenario.InitializeAsync();
        return scenario;
    }

    private async Task InitializeAsync()
    {
        var keycloak = _fixture.Keycloak;
        var doctorBSubject = await keycloak.EnsureUserAsync("test-doctor-b", "doctor");
        var patientBSubject = await keycloak.EnsureUserAsync("test-patient-b", "patient");

        DoctorA = Doctor.Create("Dr. Ana", "Psychology");
        DoctorB = Doctor.Create("Dr. Bruno", "Physiotherapy");
        PatientA = Patient.Create("Patient Alice", "alice@example.com", "555-123-4567");
        PatientB = Patient.Create("Patient Bob", "bob@example.com", "555-765-4321");

        using (var scope = _fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.AddRange(DoctorA, DoctorB, PatientA, PatientB);
            db.ApplicationIdentities.AddRange(
                Mapping(KeycloakContainer.DoctorUserId, identity => identity.LinkDoctor(DoctorA.Id)),
                Mapping(doctorBSubject, identity => identity.LinkDoctor(DoctorB.Id)),
                Mapping(KeycloakContainer.PatientUserId, identity => identity.LinkPatient(PatientA.Id)),
                Mapping(patientBSubject, identity => identity.LinkPatient(PatientB.Id)));
            db.DoctorPatientRelationships.Add(DoctorPatientRelationship.Create(DoctorA.Id, PatientA.Id));
            await db.SaveChangesAsync();
        }

        DoctorAClient = await ClientAsync("development-doctor");
        DoctorBClient = await ClientAsync("test-doctor-b");
        PatientAClient = await ClientAsync("development-patient");
        PatientBClient = await ClientAsync("test-patient-b");
        Administrator = await ClientAsync("development-administrator");
    }

    public async Task AddTreatmentAsync(DoctorId doctorId, PatientId patientId)
    {
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.DoctorPatientRelationships.Add(DoctorPatientRelationship.Create(doctorId, patientId));
        await db.SaveChangesAsync();
    }

    public IServiceScope CreateScope() => _fixture.CreateScope();

    private ApplicationIdentity Mapping(string subject, Action<ApplicationIdentity> link)
    {
        var identity = ApplicationIdentity.Create(_fixture.Keycloak.Issuer, subject);
        link(identity);
        identity.Enable();
        return identity;
    }

    private async Task<HttpClient> ClientAsync(string username)
    {
        var client = await _fixture.CreateClientAsync(username);
        _clients.Add(client);
        return client;
    }

    public void Dispose()
    {
        foreach (var client in _clients)
            client.Dispose();
    }
}
