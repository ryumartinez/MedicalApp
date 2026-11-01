using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.UnitTests.Features.Identity;

public sealed class ApplicationIdentityTests
{
    [Fact]
    public void IdentityStartsDisabledAndCanLinkBothRoles()
    {
        var identity = ApplicationIdentity.Create("https://identity.example/realms/medicalapp", "CaseSensitiveSubject");
        var doctor = DoctorId.From(Guid.CreateVersion7());
        var patient = PatientId.From(Guid.CreateVersion7());

        identity.Id.Value.Version.Should().Be(7);
        identity.Enabled.Should().BeFalse();
        identity.LinkDoctor(doctor);
        identity.LinkPatient(patient);
        identity.Enable();

        identity.Enabled.Should().BeTrue();
        identity.DoctorId.Should().Be(doctor);
        identity.PatientId.Should().Be(patient);
        identity.Subject.Should().Be("CaseSensitiveSubject");
    }

    [Theory]
    [InlineData("", "subject")]
    [InlineData(" ", "subject")]
    [InlineData("issuer", "")]
    [InlineData("issuer", " ")]
    public void IdentityRequiresIssuerAndSubject(string issuer, string subject)
    {
        Action create = () => ApplicationIdentity.Create(issuer, subject);
        create.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IncompleteIdentityCannotBeEnabledOrRemappedWhileEnabled()
    {
        var identity = ApplicationIdentity.Create("issuer", "subject");
        Action enableWithoutMapping = identity.Enable;
        enableWithoutMapping.Should().Throw<InvalidOperationException>();

        var original = DoctorId.From(Guid.CreateVersion7());
        identity.LinkDoctor(original);
        identity.Enable();
        Action change = () => identity.LinkDoctor(DoctorId.From(Guid.CreateVersion7()));
        change.Should().Throw<InvalidOperationException>();
        identity.DoctorId.Should().Be(original);

        identity.Disable();
        identity.LinkPatient(PatientId.From(Guid.CreateVersion7()));
        identity.Enabled.Should().BeFalse();
    }

    [Fact]
    public void EmptyDomainIdsCannotBeLinked()
    {
        var identity = ApplicationIdentity.Create("issuer", "subject");
        Action doctor = () => identity.LinkDoctor(DoctorId.From(Guid.Empty));
        Action patient = () => identity.LinkPatient(PatientId.From(Guid.Empty));

        doctor.Should().Throw<ArgumentException>();
        patient.Should().Throw<ArgumentException>();
    }
}
