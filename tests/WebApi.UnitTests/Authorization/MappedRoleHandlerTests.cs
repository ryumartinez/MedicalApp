using NSubstitute;
using System.Security.Claims;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace MedicalApp.UnitTests.Authorization;

public sealed class MappedRoleHandlerTests
{
    private static readonly DoctorId Doctor = DoctorId.From(Guid.CreateVersion7());
    private static readonly PatientId Patient = PatientId.From(Guid.CreateVersion7());

    [Theory]
    [InlineData(ApiRoles.Doctor, true, false, true)]
    [InlineData(ApiRoles.Doctor, false, true, false)]
    [InlineData(ApiRoles.Patient, false, true, true)]
    [InlineData(ApiRoles.Patient, true, false, false)]
    [InlineData(ApiRoles.Doctor, false, false, false)]
    public async Task RequiresResolvedRoleSpecificMapping(string role, bool doctorMapped, bool patientMapped, bool expected)
    {
        var identity = new CurrentIdentity("iss", "sub", new HashSet<string>(), ApplicationIdentityId.From(Guid.CreateVersion7()),
            doctorMapped ? Doctor : null, patientMapped ? Patient : null);

        (await Evaluate(identity, new MappedRoleRequirement(role))).Should().Be(expected);
    }

    [Fact]
    public async Task DeniesUnresolvedCaller()
    {
        (await Evaluate(null, new MappedRoleRequirement(ApiRoles.Doctor, ApiRoles.Patient))).Should().BeFalse();
    }

    [Fact]
    public async Task AcceptsEitherMappingForCombinedRequirement()
    {
        var identity = new CurrentIdentity("iss", "sub", new HashSet<string>(), null, null, Patient);

        (await Evaluate(identity, new MappedRoleRequirement(ApiRoles.Doctor, ApiRoles.Patient))).Should().BeTrue();
    }

    private static async Task<bool> Evaluate(CurrentIdentity? identity, MappedRoleRequirement requirement)
    {
        var service = Substitute.For<ICurrentIdentityService>();
        service.GetAsync(Arg.Any<CancellationToken>()).Returns(identity);
        var context = new AuthorizationHandlerContext([requirement], new ClaimsPrincipal(new ClaimsIdentity("test")), null);

        var provider = Substitute.For<IServiceProvider>();
        provider.GetService(typeof(ICurrentIdentityService)).Returns(service);
        await new MappedRoleHandler(provider).HandleAsync(context);

        return context.HasSucceeded;
    }
}
