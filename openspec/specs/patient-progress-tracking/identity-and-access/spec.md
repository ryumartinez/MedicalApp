# Identity and access Specification

## Purpose

Define Keycloak authentication, application identity mappings, treatment relationships, and resource access for patient progress tracking.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Identity and access](../../../../specs/patient-progress-tracking/identity-and-access.md).
Parent: [Patient progress tracking](../spec.md).
Milestone: 1, with authorization validation across later milestones.
Keycloak is the selected provider. Realm names, client names, provisioning, and identity mappings remain proposed first-release defaults.

## Requirements

### Requirement: Official Keycloak integrations
The AppHost MUST use `Aspire.Hosting.Keycloak` and the API MUST use `Aspire.Keycloak.Authentication` with `AddKeycloakJwtBearer`.
Implementation MUST follow the [official Aspire Keycloak documentation](https://aspire.dev/integrations/security/keycloak/).
Central package management MUST pin compatible versions, including preview versions where required, and a documented Keycloak image version.

#### Scenario: Register authentication and hosting
- **WHEN** implementation adds Keycloak to the AppHost and WebApi
- **THEN** both projects use their official integrations with compatible pinned versions and validated package APIs.

### Requirement: Realm and client separation
The proposed realm MUST be `medicalapp`, separate from the administrative `master` realm.
`medicalapp-api` MUST represent the API audience and define the `doctor` and `patient` client roles.
`medicalapp-swagger` MUST be a separate public development client with authorization code flow, PKCE, and an explicit Swagger redirect URI.

#### Scenario: Swagger authentication
- **WHEN** a developer authorizes through Swagger
- **THEN** the separate Swagger client obtains an API access token through authorization code flow with PKCE.

#### Scenario: Future frontend client
- **WHEN** the separate frontend plan configures the reserved `medicalapp-web` client
- **THEN** it selects the client type, explicit redirect URIs, and web origins for that frontend architecture.
- **AND** it uses authorization code flow with PKCE and keeps browser client secrets out of a browser-only frontend.

### Requirement: Keycloak owns credentials and tokens
Password grants and implicit flows MUST remain disabled for the application clients.
The progress API MUST NOT collect passwords or issue login or refresh tokens.

#### Scenario: Application login
- **WHEN** a caller needs an API access token
- **THEN** Keycloak handles authentication through the configured client flow.

### Requirement: API access token validation
JWT bearer validation MUST verify a trusted signature, exact realm issuer, `medicalapp-api` audience, expiration, not-before limits, and a nonempty `sub`.
The API MUST reject ID tokens and tokens that fail these checks.
Signing key rotation MUST use discovery metadata and key refresh.

#### Scenario: Invalid token
- **WHEN** a request supplies an ID token or an access token with an invalid signature, issuer, audience, lifetime, or subject
- **THEN** the API returns `401`.

#### Scenario: Signing key rotation
- **WHEN** Keycloak rotates its signing keys
- **THEN** the API refreshes trusted keys through discovery metadata to validate tokens from the configured realm.

### Requirement: Environment-specific authority configuration
Production MUST configure `Authentication:Keycloak:Authority` as an explicit HTTPS URL ending in `/realms/medicalapp` that matches the issuer.
Production MUST require HTTPS metadata and token transport.
HTTP metadata MUST require explicit development-only configuration.

#### Scenario: Missing or invalid production authority
- **WHEN** production configuration lacks an explicit HTTPS realm authority
- **THEN** startup validation fails instead of accepting a service discovery scheme as the production authority.

#### Scenario: Local HTTP metadata
- **WHEN** development explicitly permits HTTP metadata
- **THEN** the exception applies only to development and production still requires HTTPS.

### Requirement: Raw claims and API client roles
Authentication MUST preserve raw claim names through `MapInboundClaims = false` and resolve identity through validated `iss` and `sub` values.
Role normalization MUST use only `resource_access.medicalapp-api.roles` from validated tokens.
Authentication and authorization middleware MUST run before FastEndpoints.

#### Scenario: Roles from another client
- **WHEN** a token contains a doctor or patient role only for an unrelated client or an administrative role
- **THEN** those roles grant no progress permissions.

### Requirement: Unique application identity mappings
An `ApplicationIdentity` MUST store a typed ID, issuer, subject, enabled flag, and optional `DoctorId` and `PatientId` links.
PostgreSQL MUST enforce unique `(Issuer, Subject)` values and at most one identity per doctor or patient record.
One identity MUST be able to map to both domain roles.

#### Scenario: Duplicate mapping
- **WHEN** provisioning attempts a duplicate issuer-subject pair or links a doctor or patient record to another identity
- **THEN** database constraints prevent duplicate mappings.

#### Scenario: Dual-role identity
- **WHEN** one enabled identity has both domain mappings and both API client roles
- **THEN** the caller can perform each role's operations under the corresponding resource policies.

### Requirement: Enabled mappings and server-resolved actors
Doctor operations MUST require the API's doctor role and an enabled doctor mapping.
Patient operations MUST require the API's patient role and an enabled patient mapping.
Actor resolution MUST NOT use emails, usernames, display names, request fields, or custom identity headers.
Keycloak subjects MUST NOT replace domain IDs.

#### Scenario: Missing or disabled mapping
- **WHEN** an authenticated caller lacks the required role or enabled domain mapping
- **THEN** the API returns `403`.

#### Scenario: Supplied acting identity
- **WHEN** a caller supplies another patient's acting ID in a request field or header
- **THEN** the API resolves the actor from the validated identity and denies unauthorized target access.

### Requirement: Stable audit identity
`ICurrentUserService` MUST return a stable audit identifier from the mapped identity under raw claim handling.
It MUST NOT depend on an absent `ClaimTypes.NameIdentifier` claim.
`MigrationUserService` MUST retain its explicit system identity without gaining doctor or patient HTTP access.

#### Scenario: Audit a protected operation
- **WHEN** a mapped caller changes progress data
- **THEN** audit records consistently identify that mapped caller.

#### Scenario: Migration identity
- **WHEN** migrations or development seeds use the system identity
- **THEN** that identity supplies audit data without authorizing protected HTTP operations.

### Requirement: Retry-safe trusted provisioning
The proposed first release MUST use staff-managed accounts with self-registration disabled.
A trusted process MUST assign API roles and link accounts without automatic email matching.
Incomplete provisioning MUST keep the application identity disabled and permit retries without duplicate identities or domain records.
The operator procedure MUST cover creation, mapping, disabling, and recovery.

#### Scenario: Provisioning stops between systems
- **WHEN** provisioning completes only part of its Keycloak and PostgreSQL changes
- **THEN** the incomplete identity grants no progress access and a retry can finish without duplicate records.

#### Scenario: Untrusted mapping changes
- **WHEN** a patient or doctor payload attempts to assign roles or change account mappings
- **THEN** the API does not permit that change through the payload.

### Requirement: Explicit treatment relationships
PostgreSQL MUST store explicit doctor-patient treatment relationships with an active-access state.
An appointment MUST NOT establish progress access by itself.
Authorization MUST evaluate current database state for ownership, relationships, and grants.

#### Scenario: Appointment without treatment access
- **WHEN** a doctor has an appointment with a patient but no active treatment relationship
- **THEN** the appointment alone does not authorize a progress assignment or review.

### Requirement: Doctor ownership and review policies
A doctor MUST manage only their own forms and assign plans only to patients with active treatment relationships.
Review of the doctor's own plans MUST require active treatment access.
Review of another doctor's plan MUST require both an explicit plan access grant and active treatment access.

#### Scenario: Review another doctor's plan
- **WHEN** a doctor has an explicit plan access grant and an active treatment relationship with the patient
- **THEN** resource policies permit review of that plan.

#### Scenario: Incomplete review permissions
- **WHEN** either the explicit grant or the active treatment relationship is absent for another doctor's plan
- **THEN** the plan remains outside the doctor's permitted scope.

### Requirement: Patient and media resource policies
A patient MUST read assigned forms and manage drafts only within their permitted scope and submit only to their own active plans.
Lists, details, history, summaries, uploads, finalization, and downloads MUST enforce the applicable resource policies.
The API MUST authorize entry access before it grants object access.

#### Scenario: Another patient's progress
- **WHEN** a patient requests another patient's entry, history, summary, upload, finalization, or download
- **THEN** the API denies access under the same resource policies.

### Requirement: Consistent authentication and resource responses
Missing, invalid, or expired access tokens MUST return `401`.
Authenticated accounts without the required role or enabled mapping MUST receive `403`.
Detail requests for absent or out-of-scope resources MUST return `404`.
Lists and summaries MUST contain only permitted resources.

#### Scenario: Resource outside scope
- **WHEN** an otherwise authorized caller requests a detail outside their permitted scope
- **THEN** the API returns `404` without disclosing that resource.

#### Scenario: Restricted list
- **WHEN** an authorized caller requests a list or summary
- **THEN** access restrictions apply before data enters the response.

### Requirement: Documented access revocation limits
Local identity disable and treatment revocation MUST take effect at the next application authorization check.
The access token lifetime MUST bound delays after Keycloak logout, account disable, or role removal.
Previously issued signed media URLs can remain usable until expiration.

#### Scenario: Local treatment revocation
- **WHEN** a treatment relationship loses active access
- **THEN** the next application authorization check denies operations that require that relationship.

### Requirement: Persistent and repeatable local Keycloak
From milestone 1, the AppHost MUST configure `AddKeycloak`, a stable local port, `WithDataVolume()`, `WithRealmImport`, and `WithOtlpExporter()`.
The proposed port MUST be `8080` and realm files MUST reside under `tools/AppHost/Keycloak/Realms/`.
Initialization MUST preserve existing realm state and establish clients, roles, audience mapping, Swagger, and consistent development identity mappings.

#### Scenario: Restart with existing realm state
- **WHEN** local Keycloak restarts with its data volume
- **THEN** realm configuration and accounts survive and initialization does not duplicate or overwrite existing state.
- **AND** configuration updates use an explicit migration or reconciliation process because startup import does not update existing realms.

### Requirement: Keycloak secrets and bootstrap behavior
Credentials MUST use Aspire secret parameters or local user secrets.
Committed realm files MUST contain no passwords or client secrets.
The generated administrator password MUST remain in the AppHost secret store under `Parameters:keycloak-password`.
Explicit password parameters MUST use `secret: true`.

#### Scenario: Bootstrap parameters change after initialization
- **WHEN** administrator bootstrap parameters change for an existing data volume
- **THEN** persisted administrator credentials remain unchanged and a volume reset requires an explicit operator action.

### Requirement: Realm readiness and canonical issuer
The API MUST reference Keycloak through `WithReference(keycloak)` and wait through `WaitFor(keycloak)`.
Initialization MUST verify the configured realm and clients beyond the management `/health/ready` check.
Browser login, token issuance, and API validation MUST use the same canonical realm issuer.

#### Scenario: Ready process without initialized realm
- **WHEN** the Keycloak process is ready but the configured realm or clients remain unavailable
- **THEN** API initialization waits for their successful setup, including an additional initialization job when required.

#### Scenario: Host and container addresses differ
- **WHEN** browser and API requests use different network paths to Keycloak
- **THEN** their issuer configuration remains consistent and does not bypass issuer validation.

### Requirement: Separate production Keycloak configuration
Local realm file injection through `WithRealmImport` MUST remain limited to local orchestration.
Production MUST provide separate realm initialization, durable storage, database configuration, backups, TLS, and external hostname configuration.
An existing Keycloak connection can use `AddConnectionString("keycloak")` with the explicit HTTPS realm authority.

#### Scenario: Publish or deploy
- **WHEN** production configuration publishes or deploys the application
- **THEN** realm initialization does not depend on unsupported local file injection.
- **AND** a separate initialization process, infrastructure configuration, or custom image supplies the production realm configuration.

#### Scenario: Custom production image
- **WHEN** the deployment uses a custom Keycloak image with realm files
- **THEN** `WithDockerfile` preserves the official `AddKeycloak` integration's startup defaults.

## Implementation references

The source retains the [authentication registration pattern](../../../../specs/patient-progress-tracking/identity-and-access.md#api-token-validation).
It also retains the [expected file structure and responsibilities](../../../../specs/patient-progress-tracking/identity-and-access.md#expected-file-structure).

## Implementation scope

Milestone 1 includes Keycloak orchestration, realm initialization, JWT validation, claim handling, account mappings, treatment relationships, and shared authorization policies.
Each feature applies these policies to its own endpoints and queries.

Authentication configuration belongs in the WebApi host and shared infrastructure.
Application identity and treatment relationship types follow the parent spec's domain and persistence conventions.
Keycloak configuration belongs under `tools/AppHost/Keycloak/`.
Required packages use the repository's central package version management.

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
