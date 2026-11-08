using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Persistence;
using MedicalApp.Features.Accounts;
using MedicalApp.Features.Accounts.CreateAccount;
using MedicalApp.Features.Accounts.GetAccount;
using MedicalApp.Features.Accounts.LinkAccount;
using MedicalApp.Host.Extensions;
using MedicalApp.IntegrationTests.Authentication;
using MedicalApp.IntegrationTests.Common;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Authorization;

[Collection<ProgressApiCollection>]
public sealed class IdentityProvisioningTests(ProgressApiFixture fixture) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => await fixture.ResetAsync();

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    [Fact]
    public async Task NonAdministratorsCannotProvision()
    {
        var username = NewUsername();
        using var anonymous = fixture.CreateAnonymousClient();
        using var doctor = await fixture.CreateClientAsync("development-doctor");

        using var anonymousResponse = await anonymous.PostAsJsonAsync("/api/accounts", NewAccount(username), Ct);
        using var doctorResponse = await doctor.PostAsJsonAsync("/api/accounts", NewAccount(username), Ct);
        using var doctorLink = await doctor.PostAsJsonAsync($"/api/accounts/{Guid.CreateVersion7()}/links",
            new { NewDoctor = new { FullName = "Dr. Intruder", Specialty = "None" } }, Ct);

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        doctorResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        doctorLink.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await Accounts().FindByUsernameAsync(username, Ct)).Should().BeNull();
        (await CountAsync<ApplicationIdentity>()).Should().Be(0);
        (await CountAsync<Doctor>()).Should().Be(0);
    }

    [Fact]
    public async Task ProvisionsDoctorThroughRetriesAndGrantsAccessOnlyWhenEnabled()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var username = NewUsername();

        var created = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(username));
        var retried = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(username));
        retried.Id.Should().Be(created.Id);
        retried.Subject.Should().Be(created.Subject);

        var initial = await admin.GetFromJsonAsync<GetAccountResponse>($"/api/accounts/{created.Id}", Ct);
        initial!.Enabled.Should().BeFalse();
        initial.Issuer.Should().Be(TestIdentity.Issuer);
        initial.PendingSteps.Should().Contain([AccountProvisioningState.RecordLinkMissing, AccountProvisioningState.MappingDisabled]);
        initial.ProviderAccount!.PasswordSet.Should().BeFalse();

        // The account can sign in, but its disabled mapping grants nothing.
        await fixture.Identity.SetPasswordAsync(created.Subject);
        using (var incomplete = await fixture.CreateClientAsync(username))
        {
            using var denied = await incomplete.GetAsync("/api/test/doctor-policy", Ct);
            denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        using var notReady = await admin.PostAsync($"/api/accounts/{created.Id}/enable", null, Ct);
        notReady.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var newDoctor = new { NewDoctor = new { FullName = "Dr. Jane Doe", Specialty = "Psychology" } };
        var linked = await PostAsync<LinkAccountResponse>(admin, $"/api/accounts/{created.Id}/links", newDoctor);
        var relinked = await PostAsync<LinkAccountResponse>(admin, $"/api/accounts/{created.Id}/links", newDoctor);
        relinked.DoctorId.Should().Be(linked.DoctorId!.Value);
        (await CountAsync<Doctor>()).Should().Be(1);

        using var missingRole = await admin.PostAsync($"/api/accounts/{created.Id}/enable", null, Ct);
        missingRole.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await missingRole.Content.ReadAsStringAsync(Ct)).Should().Contain(AccountProvisioningState.DoctorRoleMissing);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var roles = await admin.PutAsJsonAsync($"/api/accounts/{created.Id}/roles", new { Roles = new[] { "doctor" } }, Ct);
            roles.StatusCode.Should().Be(HttpStatusCode.NoContent);
            using var enable = await admin.PostAsync($"/api/accounts/{created.Id}/enable", null, Ct);
            enable.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var complete = await admin.GetFromJsonAsync<GetAccountResponse>($"/api/accounts/{created.Id}", Ct);
        complete!.Enabled.Should().BeTrue();
        complete.PendingSteps.Should().BeEmpty();
        complete.ProviderAccount!.Roles.Should().Equal("doctor");
        complete.ProviderAccount.PasswordSet.Should().BeTrue();

        using (var provisioned = await fixture.CreateClientAsync(username))
        {
            (await provisioned.GetFromJsonAsync<Guid>("/api/test/doctor-policy", Ct)).Should().Be(linked.DoctorId.Value);
            using var patientDenied = await provisioned.GetAsync("/api/test/patient-policy", Ct);
            patientDenied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

            // Disablement takes effect at the next authorization check, even though the token is still valid.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                using var disable = await admin.PostAsync($"/api/accounts/{created.Id}/disable", null, Ct);
                disable.StatusCode.Should().Be(HttpStatusCode.NoContent);
            }

            using var afterDisable = await provisioned.GetAsync("/api/test/doctor-policy", Ct);
            afterDisable.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        }

        var disabled = await admin.GetFromJsonAsync<GetAccountResponse>($"/api/accounts/{created.Id}", Ct);
        disabled!.Enabled.Should().BeFalse();
        disabled.ProviderAccount!.Enabled.Should().BeFalse();
        (await CountAsync<ApplicationIdentity>()).Should().Be(1);
    }

    [Fact]
    public async Task CreatesAccountAndMappingTogetherOrNeither()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var username = NewUsername();
        await ExecuteSqlAsync("""
            CREATE OR REPLACE FUNCTION test_block_mapping() RETURNS trigger AS $$
            BEGIN RAISE EXCEPTION 'mapping insert blocked by test'; END $$ LANGUAGE plpgsql;
            CREATE TRIGGER test_block_mapping BEFORE INSERT ON "ApplicationIdentities" FOR EACH ROW EXECUTE FUNCTION test_block_mapping();
            """);
        try
        {
            using var failed = await admin.PostAsJsonAsync("/api/accounts", NewAccount(username), Ct);
            failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        }
        finally
        {
            await ExecuteSqlAsync("""
                DROP TRIGGER IF EXISTS test_block_mapping ON "ApplicationIdentities";
                DROP FUNCTION IF EXISTS test_block_mapping();
                """);
        }

        (await Accounts().FindByUsernameAsync(username, Ct)).Should().BeNull();
        (await CountAsync<ApplicationIdentity>()).Should().Be(0);

        var created = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(username));
        (await Accounts().FindBySubjectAsync(created.Subject, Ct))!.Username.Should().Be(username);
        (await CountAsync<ApplicationIdentity>()).Should().Be(1);
    }

    [Fact]
    public async Task RejectsAccountsThatProvisioningDidNotCreate()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var username = NewUsername();
        await Accounts().CreateAsync(new NewIdentityProviderAccount(username, $"{username}@example.invalid", "Manual", "Account"), Ct);

        using var sameUsername = await admin.PostAsJsonAsync("/api/accounts", NewAccount(username), Ct);
        using var sameEmail = await admin.PostAsJsonAsync("/api/accounts",
            NewAccount(NewUsername()) with { Email = $"{username}@example.invalid" }, Ct);
        using var developmentAccount = await admin.PostAsJsonAsync("/api/accounts", NewAccount("development-administrator"), Ct);

        sameUsername.StatusCode.Should().Be(HttpStatusCode.Conflict);
        sameEmail.StatusCode.Should().Be(HttpStatusCode.Conflict);
        developmentAccount.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountAsync<ApplicationIdentity>()).Should().Be(0);
    }

    [Fact]
    public async Task RoleChangesAppearInTokensIssuedAfterTheChange()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (created, username) = await ProvisionAsync(admin, ["doctor"], new { NewDoctor = new { FullName = "Dr. Role", Specialty = "Psychology" } });
        var before = (await fixture.Identity.GetTokensAsync(username)).AccessToken;

        using (var roles = await admin.PutAsJsonAsync($"/api/accounts/{created.Id}/roles", new { Roles = DoctorAndPatient }, Ct))
            roles.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var after = (await fixture.Identity.GetTokensAsync(username)).AccessToken;

        (await ClaimsAsync(before)).Roles.Should().Equal(ApiRoles.Doctor);
        (await ClaimsAsync(after)).Roles.Should().BeEquivalentTo([ApiRoles.Doctor, ApiRoles.Patient]);
    }

    [Fact]
    public async Task DisablingRevokesPendingCodesAndEndsSessions()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (created, username) = await ProvisionAsync(admin, ["patient"],
            new { NewPatient = new { FullName = "Pat Revoked", Email = "revoked@example.com", Phone = "555-123-4567" } });

        using var browser = fixture.CreateBrowser();
        var pkce = TestIdentity.Pkce.Create();
        var code = await TestIdentity.SignInForCodeAsync(browser, TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, pkce), username, TestIdentity.Password);

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var disable = await admin.PostAsync($"/api/accounts/{created.Id}/disable", null, Ct);
            disable.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var exchange = () => TestIdentity.ExchangeCodeAsync(browser, TestIdentity.SwaggerClientId, code, pkce);
        await exchange.Should().ThrowAsync<InvalidOperationException>().WithMessage("*invalid_grant*");

        using var authorize = await browser.GetAsync(TestIdentity.AuthorizeUrl(TestIdentity.SwaggerClientId, TestIdentity.Pkce.Create()), Ct);
        var login = TestIdentity.RedirectTarget(authorize);
        login.AbsolutePath.Should().BeEquivalentTo(IdentityServerExt.LoginPath);
        using var signIn = await TestIdentity.SubmitLoginAsync(browser, login, username, TestIdentity.Password);
        signIn.StatusCode.Should().Be(HttpStatusCode.OK);

        // Enabling the account again does not bring the revoked code back.
        using (var enable = await admin.PostAsync($"/api/accounts/{created.Id}/enable", null, Ct))
            enable.StatusCode.Should().Be(HttpStatusCode.NoContent);
        await exchange.Should().ThrowAsync<InvalidOperationException>().WithMessage("*invalid_grant*");
        (await fixture.Identity.GetTokensAsync(username)).AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task MissingPasswordDoesNotBlockEnable()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var (created, _) = await ProvisionAsync(admin, ["doctor"], new { NewDoctor = new { FullName = "Dr. Later", Specialty = "Psychology" } },
            setPassword: false);

        var account = await admin.GetFromJsonAsync<GetAccountResponse>($"/api/accounts/{created.Id}", Ct);

        account!.Enabled.Should().BeTrue();
        account.PendingSteps.Should().BeEmpty();
        account.ProviderAccount!.PasswordSet.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentRetriesCreateOneAccountAndOneRecord()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var username = NewUsername();

        var created = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(username))));
        created.Select(x => x.Id).Distinct().Should().ContainSingle();

        var newPatient = new { NewPatient = new { FullName = "Jane Doe", Email = "jane@example.com", Phone = "555-123-4567" } };
        var linked = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => PostAsync<LinkAccountResponse>(admin, $"/api/accounts/{created[0].Id}/links", newPatient)));

        linked.Select(x => x.PatientId).Distinct().Should().ContainSingle();
        (await CountAsync<ApplicationIdentity>()).Should().Be(1);
        (await CountAsync<MedicalApp.Common.Domain.Patients.Patient>()).Should().Be(1);
    }

    [Fact]
    public async Task RefusesConflictingLinks()
    {
        using var admin = await fixture.CreateClientAsync("development-administrator");
        var first = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(NewUsername()));
        var second = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(NewUsername()));
        var linked = await PostAsync<LinkAccountResponse>(admin, $"/api/accounts/{first.Id}/links",
            new { NewDoctor = new { FullName = "Dr. One", Specialty = "Psychology" } });

        using var sameDoctor = await admin.PostAsJsonAsync($"/api/accounts/{second.Id}/links", new { DoctorId = linked.DoctorId }, Ct);
        using var otherDoctor = await admin.PostAsJsonAsync($"/api/accounts/{first.Id}/links", new { DoctorId = Guid.CreateVersion7() }, Ct);
        using var missingDoctor = await admin.PostAsJsonAsync($"/api/accounts/{second.Id}/links", new { DoctorId = Guid.CreateVersion7() }, Ct);
        using var missingAccount = await admin.GetAsync($"/api/accounts/{Guid.CreateVersion7()}", Ct);

        sameDoctor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        otherDoctor.StatusCode.Should().Be(HttpStatusCode.Conflict);
        missingDoctor.StatusCode.Should().Be(HttpStatusCode.NotFound);
        missingAccount.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Creates, links, gives roles to, and enables an account, optionally with the shared test password.
    /// </summary>
    private async Task<(CreateAccountResponse Account, string Username)> ProvisionAsync(HttpClient admin, string[] roles, object link,
        bool setPassword = true)
    {
        var username = NewUsername();
        var created = await PostAsync<CreateAccountResponse>(admin, "/api/accounts", NewAccount(username));
        await PostAsync<LinkAccountResponse>(admin, $"/api/accounts/{created.Id}/links", link);
        using (var response = await admin.PutAsJsonAsync($"/api/accounts/{created.Id}/roles", new { Roles = roles }, Ct))
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        using (var response = await admin.PostAsync($"/api/accounts/{created.Id}/enable", null, Ct))
            response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        if (setPassword)
            await fixture.Identity.SetPasswordAsync(created.Subject);
        return (created, username);
    }

    private async Task<AccessTokenValidationTests.ClaimsResponse> ClaimsAsync(string accessToken)
    {
        using var client = fixture.CreateClient(accessToken);
        return (await client.GetFromJsonAsync<AccessTokenValidationTests.ClaimsResponse>("/api/test/claims", Ct))!;
    }

    private async Task ExecuteSqlAsync(string sql)
    {
        using var scope = fixture.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.ExecuteSqlRawAsync(sql, Ct);
    }

    private static readonly string[] DoctorAndPatient = [ApiRoles.Doctor, ApiRoles.Patient];

    private static string NewUsername() => $"user-{Guid.NewGuid():N}"[..20];

    private static CreateAccountRequest NewAccount(string username) =>
        new(username, $"{username}@example.com", "Test", "Account");

    private static async Task<T> PostAsync<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body, Ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{path} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(Ct)}");
        return (await response.Content.ReadFromJsonAsync<T>(Ct))!;
    }

    private IIdentityProviderAccounts Accounts() =>
        fixture.CreateScope().ServiceProvider.GetRequiredService<IIdentityProviderAccounts>();

    private async Task<int> CountAsync<T>() where T : class
    {
        using var scope = fixture.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Set<T>().CountAsync(Ct);
    }
}
