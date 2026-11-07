# Patient progress tracking: identity and access

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestone:** 1, with authorization validation across later milestones.
**Identity provider:** Keycloak.

## Scope and dependencies

Keycloak authenticates doctors and patients through OpenID Connect and issues access tokens for the API.
The API validates those tokens and applies application access rules.
PostgreSQL stores account mappings and doctor-patient relationships.

Protected endpoints depend on these identity mappings and access policies.
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.
Frontend implementation needs a separate plan.

Keycloak is the selected provider.
The implementation must follow the official [Aspire Keycloak integration documentation](https://aspire.dev/integrations/security/keycloak/).
Both the hosting and authentication integrations are required.
The realm names, client names, provisioning workflow, and mapping model in this spec are proposed first-release defaults.

## Current repository state

The template has no authentication scheme or authentication middleware.
`CurrentUserService` reads `ClaimTypes.NameIdentifier` for audit data.
`DoctorId` and `PatientId` already identify application records, with no Keycloak account mappings.

The implementation must align audit identity resolution with the token claims configuration.
`MigrationUserService` retains its explicit system identity for migrations and seed data.
That system identity does not grant doctor or patient access to HTTP endpoints.

## Keycloak realm and clients

The proposed realm is `medicalapp`.
The application uses this realm rather than Keycloak's administrative `master` realm.

| Client | Purpose | Requirements |
|---|---|---|
| `medicalapp-api` | Represents the protected API and its audience | Access tokens include this audience and the relevant API client roles. |
| `medicalapp-web` | Reserved for a future frontend | Authorization code flow with PKCE, explicit redirect URIs and web origins. Client type follows the frontend architecture. |
| `medicalapp-swagger` | Supports interactive API testing in development | Separate public client, authorization code flow with PKCE, explicit Swagger redirect URI. |

Milestone 1 configures the API and development Swagger client.
The future frontend plan configures `medicalapp-web` before browser implementation.
Password grants and implicit flows remain disabled for these clients.
The progress API does not collect passwords or issue its own login and refresh tokens.

The `medicalapp-api` client defines the `doctor` and `patient` roles.
An account can hold both roles, with the corresponding application mappings.
Keycloak administrative roles do not grant application access.

Client scopes and audience mappers must include `medicalapp-api` in the access token's `aud` claim.
A valid token for another client does not grant API access without this audience.

## API token validation

The WebApi uses `Aspire.Keycloak.Authentication` and its `AddKeycloakJwtBearer` extension to register JWT bearer authentication.
The API uses the configured realm authority and its discovery metadata and signing keys.

The required registration pattern is:

```csharp
using Microsoft.AspNetCore.Authentication.JwtBearer;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddKeycloakJwtBearer(
        serviceName: "keycloak",
        realm: "medicalapp",
        configureOptions: options =>
        {
            options.Audience = "medicalapp-api";
            options.MapInboundClaims = false;

            if (builder.Environment.IsDevelopment())
            {
                options.RequireHttpsMetadata = false;
            }
            else
            {
                options.RequireHttpsMetadata = true;
                options.Authority = builder.Configuration["Authentication:Keycloak:Authority"]
                    ?? throw new InvalidOperationException("Keycloak authority is required.");
            }
        });

builder.Services.AddAuthorization();
```

Production startup validates that `Authentication:Keycloak:Authority` is an explicit HTTPS realm URL.
The URL includes `/realms/medicalapp` and matches the token issuer.
Aspire's `https+http://` service discovery scheme does not satisfy HTTPS metadata requirements.
Production requires the explicit authority in `options.Authority`.

Token validation covers these requirements:

- The signature uses a trusted Keycloak signing key.
- The `iss` claim exactly matches the configured realm issuer.
- The `aud` claim includes `medicalapp-api`.
- The token satisfies its expiration and not-before limits.
- The token contains a nonempty `sub` claim.
- ID tokens do not authorize API requests.

The API handles signing key rotation through discovery metadata and key refresh.
Production requires HTTPS for authority metadata and token transport.
If local HTTP metadata is necessary, it requires an explicit development-only configuration.

The API preserves raw claim names with `MapInboundClaims = false`.
It resolves identity from `iss` and `sub`.
It reads API roles only from `resource_access.medicalapp-api.roles` after token validation.
The implementation normalizes those roles for ASP.NET Core authorization policies.
Roles from unrelated clients do not grant doctor or patient permissions.

Authentication and authorization middleware run before the FastEndpoints pipeline.
Every progress endpoint requires authentication and the applicable policies.
`ICurrentUserService` must return a stable audit identifier from the mapped identity.
It must not rely on an absent `ClaimTypes.NameIdentifier` claim after raw claim names take effect.

`AddKeycloakOpenIdConnect` belongs in a future API-consuming ASP.NET Core frontend if that architecture needs server-side login.
That frontend uses a cookie for browser requests and forwards access tokens to this API.
A browser-only frontend uses a public client without a browser client secret.
The frontend plan selects the architecture and corresponding client configuration.

## Application identity mappings

The proposed `ApplicationIdentity` record contains a typed ID, issuer, subject, enabled flag, and optional `DoctorId` and `PatientId` links.
PostgreSQL enforces uniqueness for `(Issuer, Subject)`.
Each doctor or patient record links to at most one application identity.
One identity can link to both a doctor and a patient record.

The mapping uses the validated `iss` and `sub` values.
Email addresses, usernames, and display names do not identify account links.
The Keycloak subject does not replace `DoctorId` or `PatientId`.

A doctor operation requires the API's `doctor` role and an enabled identity with a doctor mapping.
A patient operation requires the API's `patient` role and an enabled identity with a patient mapping.
An account with a missing or disabled mapping cannot access progress data.

Clients cannot select the acting doctor or patient through request fields or custom identity headers.
A request can identify a target patient or plan, subject to application authorization.

## Account provisioning

The proposed first release uses staff-managed doctor and patient accounts.
Self-registration remains disabled until an approved registration workflow exists.

Staff assign the required API client roles in Keycloak.
A trusted provisioning process links each Keycloak account to the existing or newly created doctor and patient records.
The API does not automatically link accounts through matching email addresses.

Provisioning must support retries without duplicate identities or domain records.
Keycloak and PostgreSQL changes do not share a database transaction.
The application identity remains disabled until its required provisioning steps finish.
An incomplete mapping grants no progress access and permits a safe provisioning retry.

The implementation needs a repeatable operator procedure for account creation, mapping, disabling, and recovery from incomplete provisioning.
The exact provisioning tool remains an open decision.
Patient and doctor request payloads cannot assign roles or change account mappings.

## Doctor-patient relationships and resource access

The application stores explicit doctor-patient treatment relationships in PostgreSQL.
A relationship identifies the doctor, patient, and whether treatment access remains active.
An appointment alone does not establish progress access.

The API applies these rules:

- A doctor can create and edit their own forms.
- A doctor can assign a form only to a patient with an active treatment relationship.
- A doctor can review their own plans only with an active treatment relationship for the patient.
- Another doctor needs an explicit plan access grant and an active treatment relationship before review.
- A patient can read assigned forms and manage their own draft entries.
- A patient can submit only to their own active plans.
- Lists, details, history, and summaries enforce the same access rules.
- Upload requests, upload finalization, and download requests require authorization against the associated entry.

Keycloak roles identify broad capabilities. They do not encode patient lists, plan ownership, or treatment relationships.
The API evaluates these resource rules against current database state.
A request parameter cannot bypass ownership or treatment access.

The [media spec](media-and-storage.md) owns private bucket configuration and temporary signed URLs.
Access policies authorize the request before the API grants access to an object.

## Access changes and response behavior

A local identity disable or treatment relationship revocation takes effect at the next application authorization check.
Previously issued JWTs can remain valid until expiration after Keycloak logout, account disable, or role removal.
The configured access token lifetime bounds that delay.
Previously issued signed media URLs can remain usable until expiration.

The API uses these response rules:

- Missing, invalid, or expired access tokens return `401`.
- Authenticated accounts without the required role or enabled application mapping receive `403`.
- Resource queries apply access restrictions before they return data.
- Detail requests return `404` for absent resources or resources outside the caller's permitted scope.
- Lists and summaries include only permitted resources.

## Keycloak and Aspire foundations

The AppHost orchestrates local Keycloak from milestone 1.

### Required packages

| Project | Package | Purpose |
|---|---|---|
| `tools/AppHost/AppHost.csproj` | [Aspire.Hosting.Keycloak](https://www.nuget.org/packages/Aspire.Hosting.Keycloak) | Models Keycloak as `KeycloakResource` and configures its container, storage, discovery, and health checks. |
| `src/WebApi/WebApi.csproj` | [Aspire.Keycloak.Authentication](https://www.nuget.org/packages/Aspire.Keycloak.Authentication) | Registers the API's authentication handler through `AddKeycloakJwtBearer`. |

The official documentation marks this integration as preview.
Package versions must be compatible with the repository's AppHost SDK and .NET target.
`Directory.Packages.props` pins compatible versions, including prerelease versions where required.
The implementation pins a documented `quay.io/keycloak/keycloak` image version and validates the selected package APIs.

### Hosting and local initialization

The AppHost creates the official resource through `AddKeycloak`.
The proposed stable local port is `8080`.
A stable port prevents browser cookies and OIDC state from referring to a different endpoint after a restart.

The AppHost uses this pattern with its existing `api` project resource:

```csharp
var keycloak = builder.AddKeycloak("keycloak", 8080)
    .WithDataVolume()
    .WithRealmImport("./Keycloak/Realms")
    .WithOtlpExporter();

api.WithReference(keycloak)
    .WaitFor(keycloak);
```

`WithDataVolume()` persists Keycloak data at `/opt/keycloak/data` outside the container lifecycle.
Local development uses this volume rather than a host-specific bind mount.
Realm JSON files belong in `tools/AppHost/Keycloak/Realms/`.
`WithRealmImport` copies these files into `/opt/keycloak/data/import` for local initialization.

Local orchestration provides these requirements:

- Persistent storage preserves realm configuration and accounts after a restart.
- Repeatable initialization establishes the realm, clients, API roles, audience mapping, and development Swagger configuration.
- Existing realm state survives initialization. Configuration updates use an explicit migration or reconciliation process.
- Development identities map consistently to seeded doctor and patient records.
- Secrets use Aspire secret parameters or local user secrets. Committed realm files contain no passwords or client secrets.
- The API waits for Keycloak readiness and completed realm initialization.
- Browser login, token issuance, and API validation use the same canonical realm issuer.
- Container addresses and host addresses do not cause issuer validation exceptions.

`AddKeycloak` supplies the default administrator username and a generated password parameter.
The generated password stays in the AppHost secret store under `Parameters:keycloak-password`.
If explicit administrator credentials are required, their password uses an `AddParameter` resource with `secret: true`.
Persisted administrator credentials survive restarts. Changes to bootstrap parameters do not reset credentials already in the volume.
Any local volume reset requires an explicit operator action.

The hosting integration supplies an HTTP readiness check against the management endpoint's `/health/ready` path.
`WaitFor(keycloak)` uses that readiness check.
The API also requires the configured realm and clients before initialization succeeds.
If an additional initialization job is necessary, it must finish before the API starts.

Realm startup import alone does not update an existing realm.
The initialization design must account for this behavior.
`WithReference(keycloak)` supplies the API's Keycloak service discovery configuration.
The API uses the realm and audience from the authentication registration and the explicit production authority configuration.
`WithOtlpExporter()` exports Keycloak telemetry to the Aspire dashboard.
Swagger supports authorization code flow with PKCE and documented API security requirements.

### Production configuration

`WithRealmImport` uses development-time file injection.
The integration does not support it with `aspire publish` or `aspire deploy`.
The AppHost applies local realm import only in local development orchestration.

Production realm configuration uses a separate initialization process, infrastructure as code, or a custom image with realm files.
A custom image keeps `AddKeycloak` and applies `WithDockerfile` to preserve the integration's startup defaults.
An initialization process can use the Keycloak Admin REST API.

An existing production Keycloak instance can use an AppHost connection string resource named `keycloak` through `AddConnectionString`.
The API references that resource and still configures its explicit HTTPS realm authority.

Production requires its own durable Keycloak deployment, database, backups, TLS, and external hostname configuration.
Local Aspire orchestration does not define that deployment.
If production uses PostgreSQL through Aspire, the optional Community Toolkit extension provides `WithPostgres` on the official Keycloak resource.
Production database configuration remains a deployment decision.

## Implementation scope

Milestone 1 includes Keycloak orchestration, realm initialization, JWT validation, claim handling, account mappings, treatment relationships, and shared authorization policies.
Each feature applies these policies to its own endpoints and queries.

Authentication configuration belongs in the WebApi host and shared infrastructure.
Application identity and treatment relationship types follow the parent spec's domain and persistence conventions.
Keycloak configuration belongs under `tools/AppHost/Keycloak/`.
Required packages use the repository's central package version management.

## Expected file structure

This tree shows the proposed files for the identity implementation.
`[N]` identifies a new file. `[M]` identifies an existing file that needs changes.
`[G]` identifies generated migration files. The timestamp comes from EF Core.

```text
MedicalApp/
├── Directory.Packages.props                              [M]
├── src/
│   └── WebApi/
│       ├── WebApi.csproj                                 [M]
│       ├── Program.cs                                    [M]
│       ├── appsettings.json                              [M]
│       ├── Host/
│       │   ├── DependencyInjection.cs                    [M]
│       │   └── Extensions/
│       │       └── KeycloakAuthenticationExt.cs           [N]
│       └── Common/
│           ├── Authentication/
│           │   └── KeycloakRoleClaimsTransformation.cs    [N]
│           ├── Authorization/
│           │   ├── ProgressPolicies.cs                   [N]
│           │   └── ProgressAuthorizationService.cs       [N]
│           ├── Interfaces/
│           │   ├── ICurrentIdentityService.cs            [N]
│           │   └── IProgressAuthorizationService.cs      [N]
│           ├── Services/
│           │   ├── CurrentIdentityService.cs             [N]
│           │   └── CurrentUserService.cs                 [M]
│           ├── Domain/
│           │   ├── ApplicationIdentities/
│           │   │   ├── ApplicationIdentity.cs            [N]
│           │   │   ├── ApplicationIdentitySpec.cs        [N]
│           │   │   └── ApplicationIdentityErrors.cs      [N]
│           │   └── DoctorPatientRelationships/
│           │       ├── DoctorPatientRelationship.cs      [N]
│           │       ├── DoctorPatientRelationshipSpec.cs  [N]
│           │       └── DoctorPatientRelationshipErrors.cs [N]
│           └── Persistence/
│               ├── VogenEfCoreConverters.cs              [M]
│               ├── Identity/
│               │   ├── ApplicationDbContext.Identity.cs  [N]
│               │   ├── ApplicationIdentityConfiguration.cs [N]
│               │   └── DoctorPatientRelationshipConfiguration.cs [N]
│               └── Migrations/
│                   ├── <timestamp>_AddProgressIdentity.cs [N, G]
│                   ├── <timestamp>_AddProgressIdentity.Designer.cs [N, G]
│                   └── ApplicationDbContextModelSnapshot.cs [M, G]
├── tools/
│   ├── AppHost/
│   │   ├── AppHost.csproj                                [M]
│   │   ├── AppHost.cs                                    [M]
│   │   └── Keycloak/
│   │       ├── README.md                                 [N]
│   │       └── Realms/
│   │           └── medicalapp-realm.json                  [N]
│   └── MigrationService/
│       ├── Program.cs                                    [M]
│       ├── Worker.cs                                     [M]
│       └── Initializers/
│           └── ProgressIdentityInitializer.cs            [N]
└── tests/
    ├── WebApi.UnitTests/
    │   └── Features/
    │       └── Identity/
    │           ├── ApplicationIdentityTests.cs            [N]
    │           └── DoctorPatientRelationshipTests.cs      [N]
    └── WebApi.IntegrationTests/
        ├── WebApi.IntegrationTests.csproj                 [M]
        ├── Common/
        │   ├── TestingDatabaseFixture.cs                 [M]
        │   ├── IntegrationTestBase.cs                    [M]
        │   └── Infrastructure/
        │       ├── Identity/
        │       │   ├── KeycloakContainer.cs              [N]
        │       │   ├── KeycloakTokenClient.cs            [N]
        │       │   └── medicalapp-test-realm.json         [N]
        │       └── Web/
        │           └── WebApiTestFactory.cs               [M]
        ├── Identity/
        │   ├── KeycloakAuthenticationTests.cs             [N]
        │   ├── ProgressAuthorizationTests.cs              [N]
        │   └── IdentityProvisioningTests.cs               [N]
        └── Persistence/
            └── Identity/
                └── IdentityPersistenceTests.cs           [N]
```

### File responsibilities

`Directory.Packages.props` pins the official Aspire Keycloak packages and required test dependencies.
The AppHost and WebApi project files reference their respective integration packages.

`AppHost.cs` configures the Keycloak resource, persistent volume, local realm import, telemetry, secrets, and startup dependencies.
`medicalapp-realm.json` defines the realm, clients, roles, audience mapping, and stable development account subjects without embedded credentials.
`Keycloak/README.md` documents local credentials, account initialization, restart behavior, and the selected realm update procedure.

`KeycloakAuthenticationExt.cs` configures `AddKeycloakJwtBearer`, authority validation, authorization registration, and Swagger security.
`Host/DependencyInjection.cs` calls that registration.
`Program.cs` places authentication and authorization before FastEndpoints.
`appsettings.json` defines non-secret Keycloak configuration. Production supplies the HTTPS authority through deployment configuration.

`KeycloakRoleClaimsTransformation.cs` normalizes roles from the validated API client claims.
`CurrentIdentityService.cs` resolves enabled application mappings through issuer and subject.
`CurrentUserService.cs` uses that resolution for stable audit identifiers.
`ProgressPolicies.cs` and `ProgressAuthorizationService.cs` define shared role, ownership, and treatment access rules.

The domain files define identity mappings, treatment relationships, typed IDs, query specifications, and errors.
Each entity file also declares its Vogen ID, consistent with existing doctor and patient entities.
The persistence files define DbSets, relationships, unique indexes, and the database migration.

`ProgressIdentityInitializer.cs` creates a development doctor, patient, identity mappings, and their active treatment relationship.
It uses stable development account subjects that match the Keycloak configuration.
`MigrationService/Program.cs` registers the initializer. `Worker.cs` runs it only in development after schema creation.
The Keycloak administrator comes from Aspire bootstrap parameters.
Development account credentials use local secrets and the selected provisioning process.

The integration fixtures provide a real Keycloak instance, tokens, authentication configuration, and authenticated HTTP clients.
Domain tests remain in the unit test project.
Authentication, persistence, provisioning, and access policy tests belong in the integration test project.

### Files that depend on remaining decisions

The trusted provisioning tool and production realm initialization method still need decisions.
Their project files, entry points, and deployment files must join this tree after those decisions.
If startup requires a separate initialization job, its files and AppHost dependencies must also join this tree.

Each progress feature adds policies to its group, endpoints, queries, and tests during its own milestone.
The corresponding feature spec owns those file lists, including plan access grant persistence and resource-specific authorization.

## Acceptance criteria

The implementation meets these criteria:

- Aspire starts Keycloak and initializes the configured realm and clients without duplicate or lost state.
- The AppHost uses `Aspire.Hosting.Keycloak`, and the API uses `Aspire.Keycloak.Authentication` with `AddKeycloakJwtBearer`.
- Keycloak uses a stable local port, a data volume, local realm import, readiness checks, and OTLP telemetry.
- Realm configuration and local accounts survive a restart.
- Production uses an explicit HTTPS authority and enables HTTPS metadata validation.
- Production configuration does not depend on `WithRealmImport` file injection.
- Swagger obtains an API access token through authorization code flow with PKCE.
- The API rejects invalid signatures, issuers, audiences, expired tokens, and ID tokens.
- Validated claims resolve the correct enabled application identity and domain mappings.
- API client roles grant only their intended doctor or patient capabilities.
- Account provisioning supports safe retries and never links accounts through email alone.
- A doctor can manage their own forms and assign plans to authorized patients.
- A patient cannot read or change another patient's progress.
- A doctor cannot read another doctor's plans without an explicit access grant and treatment access.
- Upload, finalization, and download requests enforce the same resource policies as entries.
- Audit records identify the mapped caller consistently with token claim handling.

## Validation

Unit tests cover application identity invariants and doctor-patient relationship invariants.
Integration tests cover role extraction, issuer and subject resolution, disabled identities, dual-role identities, treatment relationships, and plan access.
PostgreSQL integration tests cover identity uniqueness, doctor and patient mapping constraints, relationship changes, and provisioning retries.
Endpoint tests cover authorization for lists, details, drafts, submission, history, summaries, uploads, finalization, and downloads.
They cover the specified `401`, `403`, and `404` behavior and attempts to supply another patient's acting identity.

Keycloak integration tests use real tokens from the configured realm.
They cover audience mapping, API client roles, other-client roles, invalid issuers, invalid signatures, expiration, signing key rotation, and rejected ID tokens.
Test authentication handlers do not replace these Keycloak integration checks.
Aspire smoke checks cover realm initialization, Swagger login, canonical issuer consistency, readiness, and persistence after restart.
They also cover the stable port, mounted data volume, imported realm files, and Keycloak telemetry in the dashboard.
Configuration checks cover development-only HTTP metadata and local realm import, plus explicit HTTPS authority in production.

## Open decisions

The remaining decisions are:

- Acceptance or revision of the proposed staff-managed provisioning workflow
- The trusted provisioning tool and the staff permissions that control it
- Treatment relationship creation, revocation, and explicit plan access grant management
- Whether treatment revocation also ends existing patient plans
- Access token lifetime, refresh session policy, and acceptable revocation delay
- Keycloak image version and compatible preview package versions
- Production database configuration and realm initialization method
- Production deployment, operator access, and backup procedures.
