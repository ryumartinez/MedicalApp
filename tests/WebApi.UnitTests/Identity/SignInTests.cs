using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace MedicalApp.UnitTests.Identity;

public sealed class SignInTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    public async Task OnlyEnabledAccountsWithPasswordsCanSignIn(bool enabled, bool hasPassword, bool expected)
    {
        var user = new ApplicationUser { Id = Guid.CreateVersion7(), UserName = "someone", Enabled = enabled };
        var users = Substitute.For<UserManager<ApplicationUser>>(Substitute.For<IUserStore<ApplicationUser>>(), null, null, null, null, null, null, null, null);
        users.HasPasswordAsync(user).Returns(hasPassword);
        var signIn = new AppSignInManager(users, Substitute.For<IHttpContextAccessor>(), Substitute.For<IUserClaimsPrincipalFactory<ApplicationUser>>(),
            Options.Create(new IdentityOptions()), NullLogger<SignInManager<ApplicationUser>>.Instance,
            Substitute.For<IAuthenticationSchemeProvider>(), Substitute.For<IUserConfirmation<ApplicationUser>>());

        (await signIn.CanSignInAsync(user)).Should().Be(expected);
    }

    [Fact]
    public void SessionStartIsRecordedOnceAndSurvivesRenewal()
    {
        var properties = new AuthenticationProperties();

        SignInSession.MarkSignedIn(properties, Now);
        SignInSession.MarkSignedIn(properties, Now.AddHours(2));

        SignInSession.HasExpired(properties, Now.AddHours(8)).Should().BeFalse();
        SignInSession.HasExpired(properties, Now.AddHours(8).AddSeconds(1)).Should().BeTrue();
    }

    [Fact]
    public void SessionWithoutRecordedStartHasExpired()
    {
        SignInSession.HasExpired(new AuthenticationProperties(), Now).Should().BeTrue();
        SignInSession.HasExpired(new AuthenticationProperties(new Dictionary<string, string?> { [SignInSession.SignedInAtKey] = "soon" }), Now)
            .Should().BeTrue();
    }

    [Fact]
    public void SessionLimitsMatchThePolicy()
    {
        SignInSession.IdleTimeout.Should().Be(TimeSpan.FromMinutes(30));
        SignInSession.MaximumLifetime.Should().Be(TimeSpan.FromHours(8));
    }
}
