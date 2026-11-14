# Proposal

## Why

Keycloak adds a separate JVM service, a 300-line realm reconciler, preview-only Aspire packages, and an admin REST client to the project.
Account provisioning spans Keycloak and PostgreSQL without a shared transaction, so the API needs provisioning markers, disabled-until-complete mappings, and retry logic to stay consistent.
Production has no Keycloak deployment yet, so this is the cheapest point to move identity into the application.
ASP.NET Core Identity stores accounts in the application database, and OpenIddict issues standard OAuth 2.0 and OpenID Connect tokens from the same process.

## What Changes

- **BREAKING** The WebApi becomes its own authorization server. OpenIddict serves `/connect/authorize`, `/connect/token`, `/connect/endsession`, and discovery and key metadata. ASP.NET Core Identity stores accounts, password hashes, and API roles in the application's PostgreSQL database.
- **BREAKING** The token issuer changes from the Keycloak realm URL to the API's configured HTTPS base URL. Existing local Keycloak tokens and mappings stop working.
- Add server-rendered sign-in, sign-out, and password-setup pages. Self-registration stays unavailable.
- Access tokens carry API roles in a role claim scoped to the `medicalapp-api` resource, instead of `resource_access.medicalapp-api.roles`.
- The API validates access tokens through OpenIddict validation with the local server's keys. ID tokens, tokens without the `medicalapp-api` audience, and expired tokens are still rejected.
- Account provisioning creates the Identity account and its disabled application mapping in one database transaction. The Keycloak admin client, provisioning service account, provisioning secret, and provisioning marker are removed.
- **BREAKING** Replace the operator step "set a temporary password in the Keycloak console" with an administrator endpoint that returns a single-use, expiring password-setup link. The administrator never sees the account password.
- Disabling an account also invalidates its sign-in session and revokes outstanding authorization codes and tokens issued by the server.
- The migration service registers the API scope and the Swagger client in every environment, and seeds the development accounts with the same subject IDs as before. Development accounts get the shared password from a new `dev-account-password` Aspire secret.
- Add a one-off operator command to create the first production administrator and print its password-setup link.
- **BREAKING** Remove Keycloak from the AppHost, remove `tools/KeycloakInitializer`, the realm file, the Keycloak test container, and the `Aspire.Hosting.Keycloak` and `Aspire.Keycloak.Authentication` packages.
- Production requires an explicit HTTPS issuer, persistent signing and encryption certificates, and persistent data protection keys.

## Capabilities

### New Capabilities

None. Identity and access already owns authentication, provisioning, and local identity setup.

### Modified Capabilities

- `patient-progress-tracking/identity-and-access`: The application issues and validates its own tokens instead of trusting Keycloak. Requirements change for token validation, issuer and transport configuration, role claims, provisioning, interactive login, local initialization, development credentials, and production setup. New requirements cover the sign-in pages, password setup links, session revocation on disable, signing keys, and bootstrapping the first administrator. Mapping, resource access, treatment access, and audit requirements stay the same.

## Impact

- **WebApi**: replace `Host/Extensions/KeycloakAuthenticationExt.cs`, `Common/Authentication/KeycloakRoleClaimsTransformation.cs`, and `Common/Identity/KeycloakAccountAdministration.cs` and `KeycloakProvisioningOptions.cs`. `ApplicationDbContext` gains Identity, OpenIddict, and data protection tables through a new EF Core migration. New identity slice with `/connect/*` endpoints and Razor sign-in and password-setup pages. All `Features/Accounts` endpoints change their implementation, and a new password-setup endpoint is added. `GetAccount` reports whether a password is set.
- **Kept as is**: `ApplicationIdentity` mappings, `CurrentIdentityService`, `ProgressPolicies`, `ApiRoles` names, and all progress endpoints and their resource rules.
- **tools**: `AppHost` drops Keycloak resources and passes the issuer and development password to the API and migration service. `MigrationService` adds client, scope, role, and development account seeding, rewrites the seeded development mappings to the new issuer, and adds the administrator bootstrap command. `KeycloakInitializer` is deleted.
- **Packages**: add `OpenIddict.AspNetCore`, `OpenIddict.EntityFrameworkCore`, `Microsoft.AspNetCore.Identity.EntityFrameworkCore`, and `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`. Remove both Keycloak packages.
- **Tests**: the integration tests drop the Keycloak container and obtain tokens through the in-process authorization code flow. Keycloak-specific unit tests are replaced. Respawn keeps client registrations and roles between tests.
- **Docs**: replace `tools/AppHost/Keycloak/README.md` with an identity README, update `specs/patient-progress-tracking*.md` and `README.md`, and add an ADR for the decision.
- **Local data**: the Keycloak Docker volume is left in place and unused. Accounts provisioned against Keycloak must be provisioned again. The seeded development mappings move to the new issuer automatically.
