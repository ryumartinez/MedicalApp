using System.Net;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using MedicalApp.Features.Identity.Pages.Account;
using MedicalApp.Host.Extensions;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Authentication;

[Collection<ProgressApiCollection>]
public sealed class SignInTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task UnknownUserAndWrongPasswordShowTheSameMessage()
    {
        using var browser = fixture.CreateBrowser();
        var login = await LoginPageAsync(browser);

        using var unknown = await TestIdentity.SubmitLoginAsync(browser, login, "nobody-here", TestIdentity.Password);
        using var wrong = await TestIdentity.SubmitLoginAsync(browser, login, "development-doctor", "not-the-password");

        unknown.StatusCode.Should().Be(HttpStatusCode.OK);
        wrong.StatusCode.Should().Be(HttpStatusCode.OK);
        TestIdentity.LoginError(await unknown.Content.ReadAsStringAsync(Ct)).Should().Be(LoginModel.FailureMessage);
        TestIdentity.LoginError(await wrong.Content.ReadAsStringAsync(Ct)).Should().Be(LoginModel.FailureMessage);
    }

    [Fact]
    public async Task LocksOutAfterFiveFailures()
    {
        await fixture.Identity.EnsureUserAsync("lockout-patient", "patient");
        using var browser = fixture.CreateBrowser();
        var login = await LoginPageAsync(browser);
        for (var attempt = 0; attempt < 5; attempt++)
        {
            using var failed = await TestIdentity.SubmitLoginAsync(browser, login, "lockout-patient", "not-the-password");
            failed.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        using var locked = await TestIdentity.SubmitLoginAsync(browser, login, "lockout-patient", TestIdentity.Password);

        locked.StatusCode.Should().Be(HttpStatusCode.OK);
        TestIdentity.LoginError(await locked.Content.ReadAsStringAsync(Ct)).Should().Be(LoginModel.FailureMessage);

        // Identity checks the lockout end against the system clock, so the 15-minute period is covered by the options unit test.
        using var scope = fixture.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByNameAsync("lockout-patient");
        (await users.GetLockoutEndDateAsync(user!)).Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(15), TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task RefusesDisabledAndPasswordlessAccounts()
    {
        var disabled = await fixture.Identity.EnsureUserAsync("disabled-doctor", "doctor");
        using (var scope = fixture.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(disabled);
            user!.Enabled = false;
            (await users.UpdateAsync(user)).Succeeded.Should().BeTrue();
            await scope.ServiceProvider.GetRequiredService<IIdentityProviderAccounts>()
                .CreateAsync(new NewIdentityProviderAccount("passwordless", "passwordless@example.invalid", "No", "Password"), Ct);
        }

        using var browser = fixture.CreateBrowser();
        var login = await LoginPageAsync(browser);
        using var disabledAttempt = await TestIdentity.SubmitLoginAsync(browser, login, "disabled-doctor", TestIdentity.Password);
        using var passwordlessAttempt = await TestIdentity.SubmitLoginAsync(browser, login, "passwordless", "");
        using var guessedAttempt = await TestIdentity.SubmitLoginAsync(browser, login, "passwordless", TestIdentity.Password);

        foreach (var response in new[] { disabledAttempt, passwordlessAttempt, guessedAttempt })
        {
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            TestIdentity.LoginError(await response.Content.ReadAsStringAsync(Ct)).Should().Be(LoginModel.FailureMessage);
        }
    }

    [Fact]
    public async Task SignInCookieDoesNotAuthorizeTheApi()
    {
        using var browser = fixture.CreateBrowser();
        await SignInAsync(browser, "development-administrator");

        using var accounts = await browser.GetAsync($"/api/accounts/{Guid.CreateVersion7()}", Ct);
        using var claims = await browser.GetAsync("/api/test/claims", Ct);

        accounts.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        claims.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ActiveSessionSkipsTheSignInPage()
    {
        using var browser = fixture.CreateBrowser();
        await SignInAsync(browser, "development-doctor");

        using var again = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()), Ct);

        TestIdentity.RedirectTarget(again).GetLeftPart(UriPartial.Path).Should().Be(TestIdentity.RedirectUri);
    }

    [Fact]
    public async Task IdleSessionAsksForThePasswordAgain()
    {
        using var browser = fixture.CreateBrowser();
        await SignInAsync(browser, "development-doctor");

        fixture.Clock.Offset = SignInSession.IdleTimeout + TimeSpan.FromMinutes(1);
        using var again = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()), Ct);

        TestIdentity.RedirectTarget(again).AbsolutePath.Should().BeEquivalentTo(IdentityServerExt.LoginPath);
    }

    [Fact]
    public async Task EndSessionSignsOut()
    {
        using var browser = fixture.CreateBrowser();
        await SignInAsync(browser, "development-doctor");

        using var endSession = await browser.GetAsync("/" + IdentityServerExt.EndSessionPath, Ct);
        TestIdentity.RedirectTarget(endSession).AbsolutePath.Should().BeEquivalentTo(IdentityServerExt.SignedOutPath);
        using var again = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()), Ct);

        TestIdentity.RedirectTarget(again).AbsolutePath.Should().BeEquivalentTo(IdentityServerExt.LoginPath);
    }

    [Fact]
    public async Task OffersNoRegistration()
    {
        using var browser = fixture.CreateBrowser();
        var page = await browser.GetStringAsync(await LoginPageAsync(browser), Ct);

        page.Should().NotContainAny("register", "Register", "sign up", "Sign up", "Create account");
        foreach (var path in new[] { "/account/register", "/connect/register", "/register" })
            (await browser.GetAsync(path, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound, path);
    }

    private static async Task<Uri> LoginPageAsync(HttpClient browser)
    {
        using var authorize = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()), Ct);
        var login = TestIdentity.RedirectTarget(authorize);
        login.AbsolutePath.Should().BeEquivalentTo(IdentityServerExt.LoginPath);
        return login;
    }

    private static async Task SignInAsync(HttpClient browser, string username) =>
        await TestIdentity.SignInForCodeAsync(browser, TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()),
            username, TestIdentity.Password);
}
