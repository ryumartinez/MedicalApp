using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using MedicalApp.Common.Identity;
using MedicalApp.Features.Accounts.CreateAccount;
using MedicalApp.Features.Accounts.CreatePasswordSetupLink;
using MedicalApp.Features.Accounts.GetAccount;
using MedicalApp.Features.Identity.Pages.Account;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace MedicalApp.IntegrationTests.Authorization;

[Collection<ProgressApiCollection>]
public sealed partial class PasswordSetupLinkTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private const string ChosenPassword = "a long chosen passphrase";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task LinkSetsAPasswordThatSignsIn()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (account, username) = await CreateAccountAsync(admin);

        using var raw = await admin.PostAsync($"/api/accounts/{account.Id}/password-setup-link", null, Ct);
        (await raw.Content.ReadAsStringAsync(Ct)).Should().NotContainEquivalentOf("password\":");
        var link = await CreateLinkAsync(admin, account.Id);

        link.Url.GetLeftPart(UriPartial.Path).Should().Be($"{TestIdentity.Issuer}{CreatePasswordSetupLinkEndpoint.SetPasswordPath}");
        link.ExpiresAt.Should().BeCloseTo(fixture.Clock.GetUtcNow().AddHours(24), TimeSpan.FromMinutes(1));

        using var browser = fixture.CreateBrowser();
        var page = await OpenAsync(browser, link.Url);
        page.Headers.TryGetValues("Referrer-Policy", out var referrer).Should().BeTrue();
        referrer.Should().Equal("no-referrer");
        (await SubmitAsync(browser, link.Url, page, ChosenPassword)).Should().Be(Outcome.Completed);

        (await fixture.Identity.GetTokensAsync(username, password: ChosenPassword)).AccessToken.Should().NotBeNullOrEmpty();
        var details = await admin.GetFromJsonAsync<GetAccountResponse>($"/api/accounts/{account.Id}", Ct);
        details!.ProviderAccount!.PasswordSet.Should().BeTrue();
    }

    [Fact]
    public async Task LinksWorkOnceAndOnlyTheNewestWorks()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (account, _) = await CreateAccountAsync(admin);
        var first = await CreateLinkAsync(admin, account.Id);
        var second = await CreateLinkAsync(admin, account.Id);
        using var browser = fixture.CreateBrowser();

        (await SubmitAsync(browser, first.Url, await OpenAsync(browser, first.Url), ChosenPassword)).Should().Be(Outcome.LinkInvalid);
        var page = await OpenAsync(browser, second.Url);
        (await SubmitAsync(browser, second.Url, page, ChosenPassword)).Should().Be(Outcome.Completed);
        (await SubmitAsync(browser, second.Url, await OpenAsync(browser, second.Url), "another long passphrase")).Should().Be(Outcome.LinkInvalid);
    }

    [Fact]
    public async Task WeakPasswordKeepsTheLinkUsable()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (account, username) = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, account.Id);
        using var browser = fixture.CreateBrowser();
        using var page = await OpenAsync(browser, link.Url);

        using var weak = await PostAsync(browser, link.Url, await page.Content.ReadAsStringAsync(Ct), "short");
        var weakBody = await weak.Content.ReadAsStringAsync(Ct);
        weakBody.Should().Contain("at least 12 characters");

        (await SubmitAsync(browser, link.Url, await OpenAsync(browser, link.Url), ChosenPassword)).Should().Be(Outcome.Completed);
        (await fixture.Identity.GetTokensAsync(username, password: ChosenPassword)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExpiredLinkIsRejected()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (account, _) = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, account.Id);

        // Identity checks token age against the system clock, so shorten the lifetime the provider reads instead.
        PasswordSetupTokenProviderOptions options;
        using (var scope = fixture.CreateScope())
            options = scope.ServiceProvider.GetRequiredService<IOptions<PasswordSetupTokenProviderOptions>>().Value;
        var lifetime = options.TokenLifespan;
        options.TokenLifespan = TimeSpan.FromSeconds(1);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(2), Ct);
            using var browser = fixture.CreateBrowser();
            (await SubmitAsync(browser, link.Url, await OpenAsync(browser, link.Url), ChosenPassword)).Should().Be(Outcome.LinkInvalid);
        }
        finally
        {
            options.TokenLifespan = lifetime;
        }
    }

    [Fact]
    public async Task OnlyAdministratorsCreateLinksForExistingMappings()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        using var doctor = await fixture.CreateClientAsync("development-doctor");
        using var anonymous = fixture.CreateAnonymousClient();
        var (account, _) = await CreateAccountAsync(admin);

        using var missing = await admin.PostAsync($"/api/accounts/{Guid.CreateVersion7()}/password-setup-link", null, Ct);
        using var forbidden = await doctor.PostAsync($"/api/accounts/{account.Id}/password-setup-link", null, Ct);
        using var unauthenticated = await anonymous.PostAsync($"/api/accounts/{account.Id}/password-setup-link", null, Ct);

        missing.StatusCode.Should().Be(HttpStatusCode.NotFound);
        forbidden.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RecordedRequestTelemetryOmitsTheToken()
    {
        var recorded = new ConcurrentBag<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = recorded.Add
        };
        ActivitySource.AddActivityListener(listener);

        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (account, _) = await CreateAccountAsync(admin);
        var link = await CreateLinkAsync(admin, account.Id);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Url.Query)["token"].ToString();
        using var browser = fixture.CreateBrowser();
        await SubmitAsync(browser, link.Url, await OpenAsync(browser, link.Url), ChosenPassword);

        var setPassword = recorded.Where(activity => activity.Tags.Any(tag => tag.Value?.Contains(CreatePasswordSetupLinkEndpoint.SetPasswordPath) == true)).ToList();
        setPassword.Should().NotBeEmpty("the set-password requests must be traced for this test to mean anything");
        foreach (var activity in recorded)
        {
            activity.DisplayName.Should().NotContain(token);
            activity.Tags.Should().NotContain(tag => tag.Value != null && (tag.Value.Contains(token) || tag.Value.Contains(Uri.EscapeDataString(token))));
        }
    }

    private async Task<(CreateAccountResponse Account, string Username)> CreateAccountAsync(HttpClient admin)
    {
        var username = $"setup-{Guid.NewGuid():N}"[..20];
        using var response = await admin.PostAsJsonAsync("/api/accounts", new CreateAccountRequest(username, $"{username}@example.com", "Set", "Up"), Ct);
        response.EnsureSuccessStatusCode();
        return ((await response.Content.ReadFromJsonAsync<CreateAccountResponse>(Ct))!, username);
    }

    private static async Task<CreatePasswordSetupLinkResponse> CreateLinkAsync(HttpClient admin, Guid id)
    {
        using var response = await admin.PostAsync($"/api/accounts/{id}/password-setup-link", null, Ct);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatePasswordSetupLinkResponse>(Ct))!;
    }

    private static async Task<HttpResponseMessage> OpenAsync(HttpClient browser, Uri link)
    {
        var response = await browser.GetAsync(link.PathAndQuery, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return response;
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient browser, Uri link, string page, string password)
    {
        var antiforgery = AntiforgeryPattern().Match(page);
        var form = new Dictionary<string, string>
        {
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = password,
            ["__RequestVerificationToken"] = antiforgery.Success ? antiforgery.Groups[1].Value : ""
        };
        return await browser.PostAsync(link.PathAndQuery, new FormUrlEncodedContent(form), Ct);
    }

    private static async Task<Outcome> SubmitAsync(HttpClient browser, Uri link, HttpResponseMessage page, string password)
    {
        string html;
        using (page)
            html = await page.Content.ReadAsStringAsync(Ct);
        if (html.Contains(SetPasswordModel.InvalidLinkMessage))
            return Outcome.LinkInvalid;

        using var response = await PostAsync(browser, link, html, password);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync(Ct);
        return body.Contains("<h1>Password set</h1>") ? Outcome.Completed
            : body.Contains(SetPasswordModel.InvalidLinkMessage) ? Outcome.LinkInvalid
            : Outcome.Rejected;
    }

    [GeneratedRegex("name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]*)\"")]
    private static partial Regex AntiforgeryPattern();

    private enum Outcome
    {
        Completed,
        LinkInvalid,
        Rejected
    }
}
