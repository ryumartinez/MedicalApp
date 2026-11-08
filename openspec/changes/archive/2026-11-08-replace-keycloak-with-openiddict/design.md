# Design

## Context

See proposal.md for the motivation. This section covers only the parts of the current code that shape the approach.

- **Token validation** is in `Host/Extensions/KeycloakAuthenticationExt.cs`. It uses `AddKeycloakJwtBearer` with audience `medicalapp-api`, zero clock skew, and a `typ == "Bearer"` check that rejects ID tokens. `KeycloakRoleClaimsTransformation` rebuilds the principal so roles come only from `resource_access.medicalapp-api.roles`, under the normalized claim type `ApiRoles.ClaimType` (`medicalapp:api_role`).
- **Authorization does not depend on Keycloak.** `ProgressPolicies`, `MappedRoleHandler`, and `CurrentIdentityService` use only `iss`, `sub`, and the normalized role claim, and resolve the `ApplicationIdentity` mapping by issuer and subject. These stay unchanged.
- **Provisioning** goes through `IIdentityProviderAccounts`, which `KeycloakAccountAdministration` implements over the Keycloak admin REST API with a client-credentials service account. Keycloak and PostgreSQL share no transaction, so `CreateAccountEndpoint` stores a provisioning marker attribute on the Keycloak user, uses the marker as the mapping ID, and recovers through retries.
- **Local setup**: the AppHost runs Keycloak (run mode only), `tools/KeycloakInitializer` reconciles the realm file, and the migration service seeds mappings for the fixed subjects `01967a92-…0001` (doctor) and `…0002` (patient) under `Authentication:Keycloak:DevelopmentIssuer`. The migration service already runs in every environment, including the App Service deployment, and the API waits for it to finish.
- **Tests**: `ProgressApiFixture` starts a real Keycloak container, runs the reconciler, and gets tokens by driving the Keycloak login form. Respawn resets the `public` schema between tests, except `__EFMigrationsHistory`.
- **Conventions that apply**: every type under `Common/Domain` must inherit the domain base classes (architecture test). FastEndpoints is the endpoint style (ADR 20251018) with the global `api` route prefix. The API listens on `https://localhost:7255` locally.

## Goals / Non-Goals

**Goals:**

- Keep `ApplicationIdentity`, the issuer and subject model, the role names, the policies, and every progress endpoint unchanged.
- Make account creation a single database transaction.
- Keep local startup to `aspire start` with no manual identity steps.
- Keep the integration tests on real PostgreSQL with the real sign-in flow, without a separate identity container.

**Non-Goals:**

- Multi-factor authentication, external identity providers, email delivery, and self-service password reset. Identity supports these later without a model change.
- Refresh tokens and third-party clients. The only client is the development Swagger UI.
- A frontend application client. Its registration can be added to the same initializer later.
- Migrating Keycloak accounts. Only local development data exists.

## Decisions

### 1. Host the authorization server inside WebApi

The WebApi process runs ASP.NET Core Identity, the OpenIddict server, and OpenIddict validation.
*Alternatives:* a separate `src/AuthServer` keeps the API a pure resource server. However, provisioning would still cross two processes, and the API would need an internal admin API and a service secret, which brings back the problem this change removes. Duende IdentityServer needs a commercial license. The user chose the embedded option.

### 2. One `ApplicationDbContext` for domain, Identity, OpenIddict, and data protection

`ApplicationDbContext` derives from `IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>`, implements `IDataProtectionKeyContext`, and calls `UseOpenIddict<Guid>()`.
One EF Core migration (`AddIdentityAndOpenIddict`) adds the `AspNet*`, `OpenIddict*`, and `DataProtectionKeys` tables to the `public` schema.
`ApplicationUser : IdentityUser<Guid>` adds `Enabled` (default `false`) and lives in `Common/Identity`, not `Common/Domain`, because it does not inherit the domain base classes.
The token subject is `user.Id.ToString("D")`, so the existing development subjects and the `ApplicationIdentity.Subject` format stay valid.
*Alternative:* a separate identity context sharing the connection. It needs explicit transaction sharing in every provisioning call and a second migration history, so it was rejected.

### 3. Keep `ApplicationIdentity` as the mapping, and the Identity user as the "provider account"

The mapping still holds the doctor and patient links and the enabled flag that authorization reads. The Identity user holds credentials, roles, and sign-in state.
Merging them would touch every policy, the audit identity, and the domain tests for no change in behavior.

### 4. API roles are Identity roles, issued as a `role` claim only into access tokens

The migration service creates roles `doctor`, `patient`, and `administrator`.
At authorization time, the server adds the user's roles that are in `ApiRoles.All` as `role` claims, with only the access token as their destination.
`KeycloakRoleClaimsTransformation` becomes `ApiRoleClaimsTransformation`. It runs only for principals from the OpenIddict validation scheme, keeps `role` values in `ApiRoles.All`, maps them to `ApiRoles.ClaimType`, and drops any incoming `ApiRoles.ClaimType` claims.
`ApiRoles.ClientId` is renamed `ApiRoles.Resource` and keeps the value `medicalapp-api`.

### 5. Validate tokens with OpenIddict validation using the local server

`AddValidation(o => { o.UseLocalServer(); o.AddAudiences("medicalapp-api"); o.UseAspNetCore(); })`. The default authenticate and challenge scheme is `OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme`.
The validation handler accepts only access tokens, which covers the ID token rejection that the `typ` check used to provide. It also checks the issuer, audience, signature, and lifetime.
A validation event handler rejects tokens with an empty `sub`, in place of the current `OnTokenValidated` check.
Access tokens stay encrypted, which is the OpenIddict default. Clients treat them as opaque, and only the API reads them.
*Alternative:* keep `JwtBearer` against the server's own discovery document. It needs a back-channel call to itself and unencrypted tokens, so it was rejected.
*Clock skew:* add an integration test that proves an expired token is rejected, and configure zero skew if the validation handler allows any.

### 6. Server configuration

- Endpoints: `/connect/authorize`, `/connect/token`, and `/connect/endsession`, plus the generated `/.well-known/openid-configuration` and JWKS endpoint.
- Flows: authorization code only, with `RequireProofKeyForCodeExchange()`. The password, client credentials, implicit, and refresh token flows stay off, so OpenIddict rejects them.
- Lifetimes: access tokens 5 minutes. Authorization codes keep the 5-minute default and are single-use.
- Issuer: `SetIssuer(Authentication:Issuer)`. See decision 9.
- Passthrough is on for the authorize, token, and end-session endpoints. The handlers are FastEndpoints in `Features/Identity/Connect/` with `AllowAnonymous()`, `RoutePrefixOverride(string.Empty)`, and `ExcludeFromDescription()`.
  - **Authorize**: authenticates the Identity cookie, or challenges it to redirect to the sign-in page. It re-checks `CanSignInAsync`, builds the principal (`sub`, `name`, roles), sets the resource to `medicalapp-api` when the `medicalapp-api` scope is granted, and signs in with the OpenIddict scheme.
  - **Token**: for the code grant, it reloads the user and refuses the exchange if the user is now disabled or locked out.
  - **End session**: signs out the cookie and redirects to the registered post-logout URI.
- Transport security: `DisableTransportSecurityRequirement()` only in the Development and Testing environments, because the test server uses HTTP.

### 7. Sign-in UI with Razor Pages under the identity slice

`AddRazorPages(o => o.RootDirectory = "/Features/Identity/Pages")` with the pages `Account/Login`, `Account/SetPassword`, `Account/Logout`, and `Account/Error`. There is no registration page.
Use `AddIdentityCore<ApplicationUser>().AddRoles<IdentityRole<Guid>>().AddSignInManager().AddEntityFrameworkStores<ApplicationDbContext>().AddDefaultTokenProviders()` and register the application cookie explicitly. `AddIdentity` is not used, because it makes the cookie the default scheme and the cookie would then authorize API calls.
The cookie is HTTP-only, `Secure`, and `SameSite=Lax`, with a sliding 30-minute expiry. An `auth_time` claim enforces the 8-hour total in `OnValidatePrincipal`. `SecurityStampValidatorOptions.ValidationInterval` is zero, so a stamp change ends sessions on the next request.
A `SignInManager<ApplicationUser>` subclass overrides `CanSignInAsync` to require `Enabled` and a password.
The options are a 12-character minimum password with no composition rules, a unique email, and lockout after 5 failures for 15 minutes. Razor Pages provides antiforgery.
The login page shows one generic failure message for all failure reasons.
*Alternative:* serve the HTML pages from FastEndpoints. It would need hand-written antiforgery and form handling, so it was rejected.

### 8. Provisioning over `UserManager`, in one transaction

`KeycloakAccountAdministration` becomes `IdentityAccountAdministration : IIdentityProviderAccounts` over `UserManager` and the OpenIddict managers.
`IdentityProviderAccount` drops `ProvisioningId` and adds `HasPassword`.
`CreateAccountEndpoint` runs in an execution-strategy transaction on the shared context: it finds or creates the user with `Enabled = true` and no password, then adds the disabled mapping, then commits.
The provisioning marker is removed. A user that exists without a mapping was not created by provisioning, so the endpoint returns `AccountInUse`. A user with a mapping returns that mapping.
`SetApiRolesAsync` reconciles the Identity roles. `SetEnabledAsync` sets `Enabled` and updates the security stamp.
`LogoutAsync` updates the security stamp and revokes the subject's OpenIddict authorizations and tokens, so pending authorization codes fail.
`GetAccountResponse.ProviderAccountDto` gains `PasswordSet`. `AccountProvisioningState` is otherwise unchanged, and a missing password does not block enable.

### 9. Password setup links

`POST /api/accounts/{id}/password-setup-link` returns `{ url, expiresAt }`.
The endpoint updates the user's security stamp, which invalidates earlier links, then generates a token from a dedicated `PasswordSetupTokenProvider` (`DataProtectorTokenProvider` with `TokenLifespan` from `Authentication:PasswordSetup:Lifetime`, 24 hours by default).
The URL is `{issuer}account/set-password?user={id}&token={token}`.
`ResetPasswordAsync` changes the stamp when it succeeds, so the link works once and other sessions end.
The page sets `Referrer-Policy: no-referrer`. The token in the query string must stay out of logs and telemetry. The OpenTelemetry ASP.NET Core instrumentation redacts query values by default, and a test confirms that.
*Alternative:* an administrator-set temporary password. The user chose the link.

### 10. Issuer configuration

`Authentication:Issuer` is required outside Development and must be an absolute HTTPS URL without query, fragment, or user information. The validation replaces `ValidateAuthority`.
It is normalized once with a trailing slash by a shared helper that both the API and the migration service use, so token `iss` and stored mappings match exactly.
In Development the AppHost sets `Authentication__Issuer` on the API to its own HTTPS endpoint, and passes the same value to the migration service as `Authentication__DevelopmentIssuer`. Without Aspire, Development defaults to `https://localhost:7255/`.

### 11. Signing keys and data protection

- Development uses `AddDevelopmentSigningCertificate()` and `AddDevelopmentEncryptionCertificate()`, which persist in the user's certificate store, so tokens survive restarts.
- Testing uses ephemeral keys. The rotation test configures two signing keys.
- Production loads the certificates from `Authentication:Certificates:Signing` and `:Encryption`, each a PFX path or base64 value with an optional password. A list allows the old signing certificate to remain for validation during rotation. Startup fails when a certificate is missing.
- Data protection keys persist to `DataProtectionKeys` in every environment through `PersistKeysToDbContext<ApplicationDbContext>()`, so cookies and setup links work across instances and restarts.

### 12. Initialization in the migration service

- `IdentityClientInitializer` runs in every environment. It creates or updates the `medicalapp-api` scope with resource `medicalapp-api` and the three roles. When `Authentication:Clients:Swagger:RedirectUris` is configured, it also creates or updates the public `medicalapp-swagger` client with code-flow, PKCE, `openid`, and `medicalapp-api` permissions and those redirect URIs. It does not touch other clients.
- `DevelopmentAccountInitializer` runs only in Development. It creates `development-doctor`, `development-patient`, and `development-administrator` with fixed IDs `…0001`, `…0002`, and `…0003`, `Enabled = true`, and their roles. It sets `Authentication:DevelopmentPassword` only on accounts without a password. A username that belongs to another ID fails initialization without changing the account.
- `ProgressIdentityInitializer` reads `Authentication:DevelopmentIssuer`. When a seeded mapping exists by its fixed ID with a different issuer, it rewrites only the issuer, through `ExecuteUpdateAsync` on those two IDs. The enabled flag and links are kept.
- The shared registration helper `AddIdentityStores(IServiceCollection)` in WebApi registers Identity core and the OpenIddict core and EF stores, so the migration service and the API use the same configuration.

### 13. Bootstrap command

`dotnet run --project tools/MigrationService -- bootstrap-admin --username <u> --email <e> [--force]` runs instead of the worker.
It applies the same user and role logic, creates a user without a mapping, because administrators need none, and generates a setup link as in decision 9.
It writes the link with `Console.Out` only, never with `ILogger`, so the link does not reach OTLP.
It refuses to run when an enabled user with the `administrator` role exists, unless `--force` is given.

### 14. AppHost and packages

- Remove the Keycloak resource, `keycloak-init`, `keycloak-provisioning-secret`, `keycloak-dev-password`, `KeycloakImage.cs`, the `Realms` folder, the Keycloak README, and the `KeycloakInitializer` project and its solution entry.
- Add the `dev-account-password` parameter with the same generator settings as `keycloak-dev-password`, passed to `migrations`.
- Pass `Authentication__Issuer`, `Authentication__DevelopmentIssuer`, and `Authentication__Clients__Swagger__RedirectUris__0` from the API's HTTPS endpoint.
- Add `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` to `Directory.Packages.props`, pinned to the latest stable releases that support .NET 10. Remove `Aspire.Hosting.Keycloak` and `Aspire.Keycloak.Authentication`.

### 15. Tests

- `WebApiTestFactory` drops Keycloak and sets `Authentication:Issuer` to the test server's base address, in the Testing environment with ephemeral keys.
- A new `TestIdentity` helper creates users through `UserManager` and gets tokens through the real flow: authorize, then the login page with its antiforgery token, then the code, then the token exchange with the PKCE verifier. It uses a cookie-aware, non-redirecting client from the factory.
- Respawn adds `OpenIddictApplications`, `OpenIddictScopes`, `AspNetRoles`, and `DataProtectionKeys` to `TablesToIgnore`. The fixture runs `IdentityClientInitializer` once, and a test-only client without the API scope covers the missing-audience case.
- Tests that use time, such as token expiry and cookie idle timeout, use `TestTimeProvider` where OpenIddict and the cookie handler accept a `TimeProvider`. Otherwise they configure short lifetimes for the test.

## Risks / Trade-offs

- [The API now hosts a login surface, which raises the cost of a compromise] → Lockout, a generic failure message, antiforgery, `SameSite` cookies, no registration, and short-lived tokens. Per-IP rate limiting is listed as an open question.
- [Without email delivery, setup links pass through an administrator] → The links are single-use, expire after 24 hours, are invalidated by the next link, stay out of logs, and show no password.
- [`AddIdentity` defaults would let the cookie authorize the API] → Use `AddIdentityCore` with explicit schemes, and an integration test proves that a cookie gets `401` on the API.
- [Development certificates in the macOS user keychain may prompt or fail] → Document a fallback: set `Authentication:Certificates:*` in WebApi user secrets to a self-signed PFX.
- [Respawn could delete client registrations between tests] → Ignore those tables, and re-run the initializer in the fixture.
- [The setup token could leak through query-string telemetry] → Rely on the default redaction, and add a test that inspects recorded activity tags.
- [Keycloak features are lost: admin console, account console, and email reset] → Operators use the account endpoints and the bootstrap command. Developers use the seeded accounts and setup links.
- [Encrypted access tokens cannot be inspected in jwt.io] → Document this. Debugging uses the API's logs and claims.

## Migration Plan

1. Merge as one change. The new EF migration adds tables only, and its `Down` drops them.
2. Local developers pull and run `aspire start`. The migration service creates the tables, client, roles, and development accounts, and moves the seeded mappings to the new issuer. The development password is new: read `Parameters:dev-account-password` from the AppHost user secrets.
3. Accounts that were provisioned against Keycloak in a local database have no Identity user. Provision them again, or drop the database with the existing AppHost command.
4. The `keycloak` Docker volume is left untouched, so a rollback can use it. After the change is stable, remove it manually. The README documents the command.
5. **Rollback**: revert the change and roll back the migration (`dotnet ef database update AddProgressTracking`). Keycloak state is still in its volume. The reverted seed skips existing mappings, so the seeded mappings keep the new issuer. Drop the local database with the AppHost command so the old seed recreates them.
6. **Production**, when it is first deployed: set `Authentication:Issuer`, both certificates, and optional Swagger redirect URIs. Let the migration service run, then run `bootstrap-admin` once.

## Open Questions

- Where production keeps the certificates (Key Vault or App Service certificates), and whether data protection keys are encrypted at rest with Key Vault. Both fit decision 11 without spec changes.
- Whether to add per-IP rate limiting on `/account/login` and `/connect/token` with the built-in ASP.NET Core rate limiter. It is independent of these tasks.

## Implementation Notes

Differences between the decisions above and what was built:

- **OpenIddict 7.5.0** instead of the latest release. Releases from 7.6.0 on need EF Core 10.0.10 or later, and the repository pins EF Core 10.0.9. `System.Security.Cryptography.Pkcs` and `.Xml` are pinned to 10.0.13 to clear advisories that the data protection packages bring into non-web projects.
- **Decision 6**: transport security stays on in every environment. Tests use `https://localhost/` as the test server's base address, so `DisableTransportSecurityRequirement()` is not used. Only `S256` is accepted for PKCE: the `plain` method is removed.
- **Decision 7**: the sign-in manager is registered by the API (`IdentityServerExt`), not by `AddIdentityStores`, because it needs ASP.NET Core authentication services that the migration service does not have. The set-password page has the explicit route `/account/set-password`.
- **Decision 9**: the endpoint reads the route ID directly (`EndpointWithoutRequest`), so callers can post without a body.
- **Decision 11**: tests run in the Development environment, as before, and get ephemeral keys through `Authentication:Certificates:UseEphemeralKeys`, which only Development honors. Development also uses configured certificates when both are set, which is the macOS fallback.
- **Decision 15**:
  - Key rotation is tested on the main test host, which keeps a retired signing key after its ephemeral one.
  - Issuer, audience, subject, and rotation cases use tokens built with the server's own keys, each checked against an accepted control token.
  - Identity checks lockout and setup-token age against the system clock. The lockout test checks the recorded lockout end, and the expired-link test shortens the provider's lifetime instead of moving the test clock.
  - The integration collections run one after another: each starts an API host, and the FastEndpoints event bus resolves services from whichever host started last.
