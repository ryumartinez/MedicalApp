using System.Net.Http.Json;
using System.Text.Json.Nodes;
using KeycloakInitializer;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;

namespace MedicalApp.IntegrationTests.Authentication;

/// <summary>
/// Owns a Keycloak container so these tests can change development accounts without affecting the shared fixture.
/// </summary>
public sealed class DevelopmentAccountPasswordFixture : IAsyncLifetime
{
    public KeycloakContainer Keycloak { get; } = new();

    public ValueTask InitializeAsync() => new(Keycloak.InitializeAsync());

    public ValueTask DisposeAsync() => Keycloak.DisposeAsync();
}

// Each test changes a different account and uses its own passwords, so test order does not matter.
public sealed class DevelopmentAccountPasswordTests(DevelopmentAccountPasswordFixture fixture) : IClassFixture<DevelopmentAccountPasswordFixture>
{
    private const string UsersPath = "/admin/realms/medicalapp/users";

    private KeycloakContainer Keycloak => fixture.Keycloak;

    [Theory]
    [InlineData("")]
    [InlineData("1234567")]
    public async Task RejectsShortPasswordBeforeAnyKeycloakCall(string password)
    {
        using var client = new HttpClient(new NoRequestsHandler()) { BaseAddress = new Uri("http://localhost") };

        var reconcile = () => KeycloakRealmReconciler.ReconcileAsync(client, new JsonObject(), new string('s', 48), password);

        await reconcile.Should().ThrowAsync<InvalidOperationException>().WithMessage("*development account password*");
    }

    [Fact]
    public async Task GivesPasswordToAccountWithoutOneAndRemovesOnlyUpdatePassword()
    {
        const string userId = KeycloakContainer.DoctorUserId;
        await DeletePasswordAsync(userId);
        await SetRequiredActionsAsync(userId, "UPDATE_PASSWORD", "VERIFY_EMAIL");
        const string password = "reconciled-doctor-1";

        await Keycloak.ReconcileAsync(password);

        (await HasPasswordAsync(userId)).Should().BeTrue();
        (await GetRequiredActionsAsync(userId)).Should().Equal("VERIFY_EMAIL");
        await SetRequiredActionsAsync(userId);
        var (accessToken, _) = await Keycloak.GetTokensAsync("development-doctor", password: password);
        accessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task KeepsPasswordThatDeveloperChanged()
    {
        const string userId = KeycloakContainer.PatientUserId;
        const string changed = "changed-by-developer-1";
        using var reset = await Keycloak.Admin.PutAsJsonAsync($"{UsersPath}/{userId}/reset-password",
            new { type = "password", value = changed, temporary = false });
        reset.EnsureSuccessStatusCode();

        await Keycloak.ReconcileAsync(Keycloak.Password);

        var (accessToken, _) = await Keycloak.GetTokensAsync("development-patient", password: changed);
        accessToken.Should().NotBeNullOrEmpty();
        var withReconcilerPassword = () => Keycloak.GetTokensAsync("development-patient", password: Keycloak.Password);
        await withReconcilerPassword.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ChangedSecretLeavesExistingPasswordsUnchanged()
    {
        const string newSecret = "another-dev-secret-1";

        await Keycloak.ReconcileAsync(newSecret);

        var (accessToken, _) = await Keycloak.GetTokensAsync("development-administrator", password: Keycloak.Password);
        accessToken.Should().NotBeNullOrEmpty();
        var withNewSecret = () => Keycloak.GetTokensAsync("development-administrator", password: newSecret);
        await withNewSecret.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task LeavesAccountsOutsideRealmFileUnchanged()
    {
        using var create = await Keycloak.Admin.PostAsJsonAsync(UsersPath, new JsonObject
        {
            ["username"] = "provisioned-without-password",
            ["email"] = "provisioned-without-password@example.invalid",
            ["firstName"] = "Provisioned",
            ["lastName"] = "Account",
            ["enabled"] = true,
            ["emailVerified"] = true,
            ["requiredActions"] = new JsonArray("UPDATE_PASSWORD")
        });
        create.EnsureSuccessStatusCode();
        var userId = create.Headers.Location!.Segments[^1];

        await Keycloak.ReconcileAsync("reconciled-other-1");

        (await HasPasswordAsync(userId)).Should().BeFalse();
        (await GetRequiredActionsAsync(userId)).Should().Equal("UPDATE_PASSWORD");
    }

    private async Task<JsonArray> GetCredentialsAsync(string userId) =>
        await Keycloak.Admin.GetFromJsonAsync<JsonArray>($"{UsersPath}/{userId}/credentials")
        ?? throw new InvalidOperationException("Keycloak returned an invalid credential list.");

    private async Task<bool> HasPasswordAsync(string userId) =>
        (await GetCredentialsAsync(userId)).Any(x => x?["type"]?.GetValue<string>() == "password");

    private async Task DeletePasswordAsync(string userId)
    {
        foreach (var credential in (await GetCredentialsAsync(userId)).Where(x => x?["type"]?.GetValue<string>() == "password"))
        {
            using var delete = await Keycloak.Admin.DeleteAsync($"{UsersPath}/{userId}/credentials/{credential!["id"]!.GetValue<string>()}");
            delete.EnsureSuccessStatusCode();
        }
    }

    private async Task<string[]> GetRequiredActionsAsync(string userId)
    {
        var user = await Keycloak.Admin.GetFromJsonAsync<JsonObject>($"{UsersPath}/{userId}")
            ?? throw new InvalidOperationException("The account is missing.");
        return user["requiredActions"]?.AsArray().Select(x => x!.GetValue<string>()).ToArray() ?? [];
    }

    private async Task SetRequiredActionsAsync(string userId, params string[] actions)
    {
        var user = await Keycloak.Admin.GetFromJsonAsync<JsonObject>($"{UsersPath}/{userId}")
            ?? throw new InvalidOperationException("The account is missing.");
        user["requiredActions"] = new JsonArray(actions.Select(x => (JsonNode)x).ToArray());
        using var update = await Keycloak.Admin.PutAsJsonAsync($"{UsersPath}/{userId}", user);
        update.EnsureSuccessStatusCode();
    }

    private sealed class NoRequestsHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The reconciler must not call Keycloak before validating its arguments.");
    }
}
