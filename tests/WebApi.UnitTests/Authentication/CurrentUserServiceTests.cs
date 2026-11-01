using System.Security.Claims;
using MedicalApp.Common.Services;
using Microsoft.AspNetCore.Http;

namespace MedicalApp.UnitTests.Authentication;

public sealed class CurrentUserServiceTests
{
    [Fact]
    public void UsesMappedIdentityWhenResolved()
    {
        var context = Context("https://identity.example/realms/medicalapp", "subject");
        context.Items["MedicalApp.AuditIdentity"] = "0196a000-0000-7000-8000-000000000001";

        Service(context).UserId.Should().Be("0196a000-0000-7000-8000-000000000001");
    }

    [Fact]
    public void FallsBackToRawIssuerAndSubject()
    {
        var context = Context("https://identity.example/realms/medicalapp", "subject");

        Service(context).UserId.Should().Be("subject@https://identity.example/realms/medicalapp");
    }

    [Fact]
    public void HashesValuesThatExceedTheAuditLimit()
    {
        var issuer = "https://" + new string('a', 200) + "/realms/medicalapp";
        var first = Service(Context(issuer, "subject")).UserId;
        var second = Service(Context(issuer, "subject")).UserId;

        first.Should().StartWith("sha256:").And.HaveLength(71).And.Be(second);
    }

    [Fact]
    public void ReturnsNullWithoutAuthenticatedSubject()
    {
        var context = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "subject")])) };

        Service(context).UserId.Should().BeNull();
        Service(null).UserId.Should().BeNull();
    }

    private static DefaultHttpContext Context(string issuer, string subject) => new()
    {
        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("iss", issuer), new Claim("sub", subject)], "test"))
    };

    private static CurrentUserService Service(HttpContext? context) =>
        new(new HttpContextAccessor { HttpContext = context });
}
