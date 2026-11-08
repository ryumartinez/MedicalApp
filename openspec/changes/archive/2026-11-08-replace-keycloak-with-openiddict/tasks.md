# Tasks

## 1. Packages and schema

- [x] 1.1 Add `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` to `Directory.Packages.props`, pinned to the latest stable releases that support .NET 10, and reference them from `src/WebApi/WebApi.csproj`. Verify with `dotnet build` and no new `NU19xx` vulnerability warnings.
- [x] 1.2 Add `Common/Identity/ApplicationUser.cs` (`IdentityUser<Guid>` with `Enabled`, default `false`). Make `ApplicationDbContext` derive from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, implement `IDataProtectionKeyContext`, and call `UseOpenIddict<Guid>()` (design decision 2). Verify that the architecture tests still pass, which confirms `ApplicationUser` is outside `Common/Domain`.
- [x] 1.3 Add the `AddIdentityAndOpenIddict` EF Core migration. Verify that it only creates the `AspNet*`, `OpenIddict*`, and `DataProtectionKeys` tables, that `dotnet ef migrations script AddProgressTracking AddIdentityAndOpenIddict` shows no change to existing tables, and that `IdentityPersistenceTests` pass.
- [x] 1.4 Add `OpenIddictApplications`, `OpenIddictScopes`, `AspNetRoles`, and `DataProtectionKeys` to Respawn's `TablesToIgnore` in `TestDatabase.cs`. Verify that the full integration suite still passes.

## 2. Shared identity configuration

- [x] 2.1 Add the shared issuer helper (design decision 10). It reads `Authentication:Issuer`, requires an absolute HTTPS URL without query, fragment, or user information outside Development, defaults Development to `https://localhost:7255/`, and normalizes the trailing slash. Replace `KeycloakAuthorityTests` with `IssuerConfigurationTests` that cover missing, HTTP, query, fragment, user-info, and valid values in Production and Development.
- [x] 2.2 Add `AddIdentityStores(IServiceCollection)` (design decision 12). It registers `AddIdentityCore<ApplicationUser>` with roles, the custom `SignInManager`, EF stores, default token providers, the `PasswordSetupTokenProvider`, and the password and lockout options from design decision 7, plus OpenIddict core with EF stores and `Guid` keys. Verify with a unit test that the resolved `IdentityOptions` have a 12-character minimum, no composition rules, a unique email, and lockout after 5 failures for 15 minutes.
- [x] 2.3 Add the signing and encryption key setup (design decision 11). Development uses development certificates, Testing uses ephemeral keys, and other environments load `Authentication:Certificates:Signing` and `:Encryption` (one or more). Persist data protection keys to `ApplicationDbContext`. Verify with a unit test that Production startup without certificates fails with a message naming the missing setting.

## 3. Identity initialization in the migration service

- [x] 3.1 Add `IdentityClientInitializer`. It runs in every environment and creates or updates the `medicalapp-api` scope and resource, the three roles, and the `medicalapp-swagger` client when `Authentication:Clients:Swagger:RedirectUris` is set. Register it in `tools/MigrationService/Program.cs` and `Worker.cs` before the seeders. Add integration tests for:
  - A fresh database.
  - A second run, which must create no duplicates.
  - A changed redirect URI, which must replace the old one.
  - An unrelated client, which must stay unchanged.
  - No redirect URIs configured, which must create no client.
- [x] 3.2 Add `DevelopmentAccountInitializer`, which runs in Development only. It creates the three accounts with fixed IDs `…0001`, `…0002`, and `…0003`, `Enabled = true`, and their roles. It sets `Authentication:DevelopmentPassword` only on accounts without a password, and fails without changes when a development username belongs to another ID. Port `DevelopmentAccountPasswordTests` to cover:
  - Fresh accounts get the password.
  - A changed password is kept.
  - A changed secret does not overwrite existing passwords.
  - A provisioned account without a password stays without one.
  - A short password is rejected.
- [x] 3.3 Change `ProgressIdentityInitializer` to read `Authentication:DevelopmentIssuer` and to rewrite only the issuer of the two seeded mappings, by their fixed IDs, when it differs. Extend `ProgressIdentitySeedTests` with three cases:
  - A mapping with the Keycloak issuer moves to the new issuer and keeps its links.
  - A disabled seeded mapping stays disabled.
  - A repeat run changes nothing.

## 4. Authorization server, sign-in, and token validation

- [x] 4.1 Add the OpenIddict server and validation setup that replaces `AddKeycloakAuthentication` (design decisions 5 and 6), as `AddIdentityServer` and `AddTokenValidation` in `Host/Extensions/`:
  - Code flow with PKCE only, a 5-minute access token lifetime, no refresh tokens, the configured issuer, and passthrough endpoints.
  - Validation scheme as the default, with the `medicalapp-api` audience and an empty-`sub` rejection.
  - Transport security disabled only in Development and Testing.

  Verify that the app starts and that `/.well-known/openid-configuration` lists only the `authorization_code` grant and requires `S256`.
- [x] 4.2 Register the application cookie explicitly (design decision 7): HTTP-only, `Secure`, `SameSite=Lax`, a sliding 30-minute expiry, an 8-hour `auth_time` limit, and zero security stamp validation interval. Add `AppSignInManager` that requires `Enabled` and a password. Verify with unit tests for `CanSignInAsync` and the 8-hour check.
- [x] 4.3 Add the `Features/Identity/Connect/` FastEndpoints for authorize, token, and end session (`AllowAnonymous`, no route prefix, excluded from Swagger), following design decision 6. Verify through the tests in 4.7.
- [x] 4.4 Add Razor Pages under `Features/Identity/Pages/Account/` for `Login`, `Logout`, and `Error`. The login page shows one generic failure message, uses `lockoutOnFailure: true`, and has no registration link. Register `AddRazorPages` and `MapRazorPages` in `Program.cs`. Verify through the tests in 4.7.
- [x] 4.5 Replace `KeycloakRoleClaimsTransformation` with `ApiRoleClaimsTransformation` (design decision 4), and rename `ApiRoles.ClientId` to `ApiRoles.Resource`. Have the authorize endpoint add only `ApiRoles.All` roles, with the access token as their only destination. Port `KeycloakRoleClaimsTransformationTests` to cover:
  - Unknown roles are dropped.
  - Injected `medicalapp:api_role` claims are dropped.
  - Both roles are kept.
  - Principals from other schemes are left unchanged.
- [x] 4.6 Point Swagger at `{issuer}connect/authorize` and `{issuer}connect/token`, rename the security scheme from `Keycloak`, and request `openid medicalapp-api`. Remove `KeycloakAuthenticationExt.cs`. Verify that `SwaggerAuthorizationTests` (4.7) pass.
- [x] 4.7 Add the `TestIdentity` test helper (design decision 15) and switch `ProgressApiFixture.CreateClientAsync` and `WebApiTestFactory` to it. Keep the Keycloak container only for `IdentityProvisioningTests` until group 5. Port `KeycloakAuthenticationTests` to `AccessTokenValidationTests`, which covers:
  - Valid, missing, forged, and ID tokens.
  - A token from another issuer.
  - A token without the API audience, from a test client without the scope.
  - An expired token without skew.
  - A token from a rotated key.
- [x] 4.8 Port `SwaggerAuthorizationTests` and add `SignInTests`. Between them, cover:
  - The PKCE sign-in round trip.
  - Rejection of an unregistered redirect URI, a request without PKCE, the password grant, and `offline_access`.
  - The same failure message for an unknown user and a wrong password.
  - Lockout after 5 failures.
  - A disabled or passwordless account cannot sign in.
  - A sign-in cookie on a progress endpoint gets `401`.
  - An idle session asks for the password again.
  - End session signs out.

  Verify with `dotnet test tests/WebApi.IntegrationTests` and `dotnet test tests/WebApi.UnitTests`.

## 5. Provisioning over Identity

- [x] 5.1 Replace `KeycloakAccountAdministration` and `KeycloakProvisioningOptions` with `IdentityAccountAdministration` (design decision 8). Remove `ProvisioningId` from `IdentityProviderAccount` and add `HasPassword`. `LogoutAsync` updates the security stamp and revokes the subject's OpenIddict authorizations and tokens. Verify that the solution builds with no `Keycloak` references under `src/`.
- [x] 5.2 Rewrite `CreateAccountEndpoint` to create the user and the disabled mapping in one execution-strategy transaction, and to return `AccountInUse` for an existing user without a mapping. Update the `AccountsGroup` XML comment. Extend `IdentityProvisioningTests` with:
  - A failed mapping insert leaves no user behind.
  - A retry returns the same mapping.
  - An unmapped username or email returns `409`.
- [x] 5.3 Update `SetAccountRoles`, `EnableAccount`, `DisableAccount`, and `GetAccount` for the new implementation, and add `PasswordSet` to `GetAccountResponse.ProviderAccountDto`. Extend `IdentityProvisioningTests` with:
  - The role change appears in the next token.
  - Disabling revokes an unused authorization code and ends the sign-in session.
  - A repeated disable succeeds.
  - Enabling again does not restore revoked codes.
  - A missing password does not block enable.
- [x] 5.4 Add the `Accounts/CreatePasswordSetupLink` slice: `POST /api/accounts/{id}/password-setup-link`, returning `{ url, expiresAt }`, as in design decision 9. Add the `Account/SetPassword` Razor page with `Referrer-Policy: no-referrer`. Add integration tests for:
  - A link sets a password that then signs in.
  - A reused link, a replaced link, and an expired link are rejected.
  - A weak password keeps the link usable.
  - An unknown mapping returns `404`.
  - A non-administrator gets `403`.
  - Recorded HTTP activity tags do not contain the token.
- [x] 5.5 Remove `KeycloakContainer.cs` and the `KeycloakInitializer` project reference and realm file content from `WebApi.IntegrationTests.csproj`. Verify that the full integration suite passes with no Keycloak container started.

## 6. Administrator bootstrap

- [x] 6.1 Add the `bootstrap-admin --username --email [--force]` mode to `tools/MigrationService/Program.cs` (design decision 13). It writes the setup link only with `Console.Out`. Add integration tests for these cases:
  - An empty database gets an administrator, and a link is printed.
  - A second run without `--force` fails and creates nothing.
  - A run with `--force` creates the account.
  - No logger output contains the link.

## 7. Orchestration cleanup and documentation

- [x] 7.1 Update `tools/AppHost/AppHost.cs` (design decision 14):
  - Remove Keycloak, `keycloak-init`, and the Keycloak parameters.
  - Add `dev-account-password` and pass it to `migrations` as `Authentication__DevelopmentPassword`.
  - Pass `Authentication__Issuer` to the API, `Authentication__DevelopmentIssuer` to `migrations`, and the Swagger redirect URI to `migrations`, all from the API's HTTPS endpoint.

  Verify that the AppHost builds.
- [x] 7.2 Delete `tools/KeycloakInitializer/`, `tools/AppHost/Keycloak/`, and their `MedicalApp.slnx` and `AppHost.csproj` entries. Remove `Aspire.Hosting.Keycloak` and `Aspire.Keycloak.Authentication` from `Directory.Packages.props` and the project files. Verify that `git grep -n -i keycloak -- src tools tests Directory.Packages.props MedicalApp.slnx` returns only the issuer-move code and its tests.
- [x] 7.3 Write `src/WebApi/Features/Identity/README.md`, which replaces the Keycloak README. Cover:
  - Endpoints, clients, and lifetimes.
  - The development accounts and how to read or override `dev-account-password` with `dotnet user-secrets`.
  - The provisioning steps with the setup link.
  - The disable and revocation limits.
  - Production settings and certificate rotation.
  - `bootstrap-admin`.
  - The macOS certificate fallback.
  - How to remove the old `keycloak` volume.

  Verify that every command in it runs as written.
- [x] 7.4 Update `specs/patient-progress-tracking.md`, `specs/patient-progress-tracking/identity-and-access.md`, and `README.md` to describe Identity and OpenIddict instead of Keycloak. Add the ADR `docs/adr/20261108-use-aspnet-core-identity-and-openiddict-instead-of-keycloak.md` from `docs/adr/template.md`. Verify that the ADR index lists it.

## 8. Integration checks

- [x] 8.1 On an existing local database with seeded Keycloak-issuer mappings, run `aspire start`. Confirm three things:
  - The migration service completes.
  - `development-doctor` signs in through Swagger at `https://localhost:7255/swagger` with `Parameters:dev-account-password`.
  - A doctor progress endpoint returns success.
- [x] 8.2 Restart the AppHost. Confirm that a token issued before the restart still works until it expires, and that the sign-in and the doctor endpoint still succeed.
- [x] 8.3 Provision a new patient account through `/api/accounts`, including a setup link. Set its password in the browser, sign in, and call a patient endpoint. Then disable it and confirm that the next call returns `403` and a new sign-in fails.
- [x] 8.4 Run `dotnet run --project tools/AppHost -- --operation publish --publisher manifest --output-path ./publish/aspire-manifest.json`. Confirm that the output does not mention `keycloak`, `dev-account-password`, or a development redirect URI.

## Workflow follow-up

- At archive, update the Purpose of `openspec/specs/patient-progress-tracking/identity-and-access/spec.md`, which still says "through Keycloak".
- Archive the change after review, and confirm that the main spec no longer contains the two removed Keycloak requirements.
