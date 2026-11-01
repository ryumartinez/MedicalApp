using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Persistence.Identity;

public sealed class IdentityPersistenceTests : IntegrationTestBase
{
    private readonly TestingDatabaseFixture _fixture;

    public IdentityPersistenceTests(TestingDatabaseFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task IssuerAndSubjectMustBeUnique()
    {
        await AddAsync(ApplicationIdentity.Create("https://identity.example/realms/medicalapp", "subject"));
        Func<Task> duplicate = () => AddAsync(ApplicationIdentity.Create("https://identity.example/realms/medicalapp", "subject"));

        await duplicate.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task DoctorAndPatientCanEachLinkToOnlyOneIdentity()
    {
        var doctor = Doctor.Create("Dr. Smith", "Cardiology");
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        await AddAsync(doctor);
        await AddAsync(patient);
        var first = ApplicationIdentity.Create("issuer", "first");
        first.LinkDoctor(doctor.Id);
        first.LinkPatient(patient.Id);
        await AddAsync(first);

        using var doctorScope = _fixture.CreateScope();
        var doctorDb = doctorScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = ApplicationIdentity.Create("issuer", "second");
        second.LinkDoctor(doctor.Id);
        doctorDb.ApplicationIdentities.Add(second);
        Func<Task> duplicateDoctor = () => doctorDb.SaveChangesAsync(CancellationToken);
        await duplicateDoctor.Should().ThrowAsync<UniqueConstraintException>();

        using var patientScope = _fixture.CreateScope();
        var patientDb = patientScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var third = ApplicationIdentity.Create("issuer", "third");
        third.LinkPatient(patient.Id);
        patientDb.ApplicationIdentities.Add(third);
        Func<Task> duplicatePatient = () => patientDb.SaveChangesAsync(CancellationToken);
        await duplicatePatient.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task TreatmentIsUniqueAndRetainsItsPartiesAfterRevocation()
    {
        var doctor = Doctor.Create("Dr. Smith", "Cardiology");
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        await AddAsync(doctor);
        await AddAsync(patient);
        var treatment = DoctorPatientRelationship.Create(doctor.Id, patient.Id);
        await AddAsync(treatment);
        treatment.Revoke();
        await SaveAsync();

        var saved = await GetQueryable<DoctorPatientRelationship>().SingleAsync(CancellationToken);
        saved.IsActive.Should().BeFalse();
        saved.DoctorId.Should().Be(doctor.Id);
        saved.PatientId.Should().Be(patient.Id);

        Func<Task> duplicate = () => AddAsync(DoctorPatientRelationship.Create(doctor.Id, patient.Id));
        await duplicate.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task TreatmentRequiresExistingDoctorAndPatient()
    {
        var treatment = DoctorPatientRelationship.Create(DoctorId.From(Guid.CreateVersion7()), PatientId.From(Guid.CreateVersion7()));
        Func<Task> save = () => AddAsync(treatment);

        await save.Should().ThrowAsync<ReferenceConstraintException>();
    }
}
