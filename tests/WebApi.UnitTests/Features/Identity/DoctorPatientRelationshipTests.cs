using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.UnitTests.Features.Identity;

public sealed class DoctorPatientRelationshipTests
{
    [Fact]
    public void TreatmentCanBeRevokedAndReactivatedWithoutChangingItsParties()
    {
        var doctor = DoctorId.From(Guid.CreateVersion7());
        var patient = PatientId.From(Guid.CreateVersion7());
        var treatment = DoctorPatientRelationship.Create(doctor, patient);

        treatment.Id.Value.Version.Should().Be(7);
        treatment.IsActive.Should().BeTrue();
        treatment.Revoke();
        treatment.IsActive.Should().BeFalse();
        treatment.Reactivate();
        treatment.IsActive.Should().BeTrue();
        treatment.DoctorId.Should().Be(doctor);
        treatment.PatientId.Should().Be(patient);
    }

    [Fact]
    public void TreatmentRequiresBothDomainIds()
    {
        var doctor = DoctorId.From(Guid.CreateVersion7());
        var patient = PatientId.From(Guid.CreateVersion7());
        Action missingDoctor = () => DoctorPatientRelationship.Create(DoctorId.From(Guid.Empty), patient);
        Action missingPatient = () => DoctorPatientRelationship.Create(doctor, PatientId.From(Guid.Empty));

        missingDoctor.Should().Throw<ArgumentException>();
        missingPatient.Should().Throw<ArgumentException>();
    }
}
