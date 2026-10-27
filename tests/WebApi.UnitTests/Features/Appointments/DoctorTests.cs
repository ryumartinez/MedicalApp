using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Domain.Doctors;

namespace MedicalApp.UnitTests.Features.Appointments;

public class DoctorTests
{
    [Fact]
    public void Create_ShouldGenerateIdAndNormalizeDetails()
    {
        var doctor = Doctor.Create("  Dr. Smith  ", "  Cardiology  ");

        doctor.Id.Value.Version.Should().Be(7);
        doctor.FullName.Should().Be("Dr. Smith");
        doctor.Specialty.Should().Be("Cardiology");
    }

    [Fact]
    public void SettersAndUpdateMethods_ShouldEnforceInvariants()
    {
        var doctor = Doctor.Create("Dr. Smith", "Cardiology");

        Action name = () => doctor.FullName = new string('n', Doctor.FullNameMaxLength + 1);
        Action specialty = () => doctor.Specialty = new string('s', Doctor.SpecialtyMaxLength + 1);
        Action updateName = () => doctor.UpdateFullName(" ");
        Action updateSpecialty = () => doctor.UpdateSpecialty(" ");

        name.Should().Throw<ArgumentException>();
        specialty.Should().Throw<ArgumentException>();
        updateName.Should().Throw<ArgumentException>();
        updateSpecialty.Should().Throw<ArgumentException>();
        doctor.FullName.Should().Be("Dr. Smith");
        doctor.Specialty.Should().Be("Cardiology");
    }
}
