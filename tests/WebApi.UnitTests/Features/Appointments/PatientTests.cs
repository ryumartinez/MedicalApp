using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.UnitTests.Features.Appointments;

public class PatientTests
{
    [Fact]
    public void Create_ShouldGenerateIdAndNormalizeContactDetails()
    {
        var patient = Patient.Create("  Jane Doe  ", "JANE@EXAMPLE.COM", "  +1-555-123-4567  ");

        patient.Id.Value.Version.Should().Be(7);
        patient.FullName.Should().Be("Jane Doe");
        patient.Email.Should().Be("jane@example.com");
        patient.Phone.Should().Be("+1-555-123-4567");
    }

    [Theory]
    [InlineData("", "jane@example.com", "555-123-4567")]
    [InlineData("Jane", "invalid", "555-123-4567")]
    [InlineData("Jane", "jane@example.com", "invalid")]
    public void Create_WithInvalidDetails_ShouldThrow(string name, string email, string phone)
    {
        Action act = () => Patient.Create(name, email, phone);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Updates_ShouldEnforceLimitsAndPreserveExistingValuesOnFailure()
    {
        var patient = Patient.Create("Jane", "jane@example.com", "555-123-4567");

        Action name = () => patient.UpdateFullName(new string('n', Patient.FullNameMaxLength + 1));
        Action email = () => patient.UpdateEmail(new string('e', Patient.EmailMaxLength) + "@example.com");
        Action phone = () => patient.UpdatePhone(new string('1', Patient.PhoneMaxLength + 1));

        name.Should().Throw<ArgumentException>();
        email.Should().Throw<ArgumentException>();
        phone.Should().Throw<ArgumentException>();
        patient.FullName.Should().Be("Jane");
        patient.Email.Should().Be("jane@example.com");
        patient.Phone.Should().Be("555-123-4567");
    }
}
