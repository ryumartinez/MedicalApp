# identity-and-access Specification

## Purpose

Authenticate doctors and patients through Keycloak and restrict progress data to callers with valid application mappings and resource access.

## Requirements

### Requirement: Validate API access tokens

The API SHALL accept only access tokens with a trusted signature, the configured realm issuer, the `medicalapp-api` audience, valid time limits, and a nonempty subject.
Every progress endpoint MUST require authentication.
ID tokens MUST NOT authorize API requests.

#### Scenario: Accept a valid access token

- **WHEN** A caller presents a correctly signed access token from the configured realm with the required audience, subject, and time limits.
- **THEN** The API authenticates the caller and evaluates application authorization.

#### Scenario: Reject invalid authentication

- **WHEN** A request has no token, an invalid signature or issuer, a missing audience or subject, invalid time limits, or an ID token.
- **THEN** The API returns `401` and exposes no progress data.

#### Scenario: Accept a rotated signing key

- **WHEN** Keycloak rotates its signing key and publishes the new key through trusted discovery metadata.
- **THEN** The API refreshes its keys and accepts valid access tokens signed with that key.

### Requirement: Preserve production transport and issuer validation

Production SHALL require an explicit HTTPS realm authority and HTTPS metadata validation.
The authority MUST include `/realms/medicalapp` and match the token issuer.
Local HTTP metadata MUST require explicit development configuration.

#### Scenario: Validate production configuration

- **WHEN** Production starts without an explicit HTTPS realm authority or with an invalid realm URL.
- **THEN** Startup rejects the configuration.

#### Scenario: Use a consistent local issuer

- **WHEN** A developer obtains a token through the browser and calls the locally orchestrated API.
- **THEN** Browser login, token issuance, and API validation agree on the canonical realm issuer.
- **AND** Host and container addresses do not require an issuer validation exception.

### Requirement: Scope roles to the API client

The API SHALL read doctor, patient, and administrator roles only from `resource_access.medicalapp-api.roles` in a validated token.
One account can hold both roles.
Unrelated client roles and Keycloak administrative roles MUST NOT grant application permissions.

#### Scenario: Ignore unrelated roles

- **WHEN** A valid token has doctor or administrative roles outside the API client's role collection.
- **THEN** Those roles grant no doctor or patient progress permissions.

#### Scenario: Support an account with both roles

- **WHEN** A caller has both API roles and enabled doctor and patient mappings.
- **THEN** The caller can use each role within its corresponding resource scope.

#### Scenario: Require the administrator API role

- **WHEN** A caller requests an administrator operation without the administrator API role.
- **THEN** The API denies the operation and changes no account, treatment relationship, or grant.

### Requirement: Map identity through issuer and subject

The application SHALL identify accounts through the validated issuer and subject pair.
The pair MUST be unique.
Each doctor or patient record MUST link to at most one application identity.
One identity can link to both record types.
Email, username, and display name MUST NOT establish account links.

#### Scenario: Resolve an existing mapping

- **WHEN** A caller presents the issuer and subject of an enabled mapped identity.
- **THEN** The application resolves the linked doctor and patient records without replacing their domain identifiers.

#### Scenario: Reject duplicate mappings

- **WHEN** Provisioning attempts a duplicate issuer and subject pair or a second identity for an already linked doctor or patient.
- **THEN** Persistence rejects the conflicting mapping.

#### Scenario: Prevent email-based account linking

- **WHEN** An unmapped account has the same email address as an existing doctor or patient.
- **THEN** The application does not create an identity link through that email match.

### Requirement: Require an enabled role-specific mapping

A doctor operation SHALL require the API doctor role and an enabled doctor mapping.
A patient operation SHALL require the API patient role and an enabled patient mapping.
Client fields and custom identity headers MUST NOT select the acting doctor or patient.

#### Scenario: Deny a missing or disabled mapping

- **WHEN** An authenticated caller lacks the required API role or enabled role-specific mapping.
- **THEN** The API returns `403`.

#### Scenario: Reject an acting identity override

- **WHEN** A patient supplies another patient's identifier through an acting identity field or custom header.
- **THEN** The API uses the caller's mapped identity and grants no additional access.

### Requirement: Provide administrator account endpoints

The API SHALL provide administrator endpoints for account creation, linking, role assignment, disablement, and recovery.
Only designated administrators with the administrator API role MUST use these endpoints.

#### Scenario: Reject unauthorized provisioning

- **WHEN** A caller without administrator authorization requests an account operation.
- **THEN** The API changes no account, role, or mapping.

### Requirement: Provision accounts through trusted staff operations

The first release SHALL use staff-managed accounts with self-registration disabled.
Provisioning MUST support retries without duplicate accounts, mappings, or domain records.
Mappings MUST remain disabled until all required provisioning steps finish.
Doctor and patient request payloads MUST NOT assign roles or change account mappings.

#### Scenario: Recover incomplete provisioning

- **WHEN** Keycloak account creation succeeds but application provisioning remains incomplete.
- **THEN** The mapping grants no progress access.
- **AND** A trusted retry can finish provisioning without duplicate identities or domain records.

#### Scenario: Manage an account operationally

- **WHEN** An operator provisions, links, disables, or recovers an account.
- **THEN** A documented procedure identifies the required trusted permissions and recovery steps.

### Requirement: Manage treatment and plan access through administrator endpoints

The API SHALL provide administrator endpoints to create and revoke treatment relationships and plan access grants.
Only designated administrators with the administrator API role MUST use these endpoints.
Revoking a treatment relationship MUST close the affected doctor's plans for that patient.
The operation MUST preserve plan entries and coordinate with concurrent draft changes and submissions.
Plans owned by other doctors MUST remain unchanged.

#### Scenario: Revoke an owner's treatment relationship

- **WHEN** An administrator revokes a doctor's treatment relationship with a patient.
- **THEN** The doctor's active plans for that patient close and retain their entries.
- **AND** Other doctors' plans for the patient remain unchanged.

#### Scenario: Revoke a reviewing doctor's treatment relationship

- **WHEN** An administrator revokes the treatment relationship of a doctor who has a grant to another doctor's plan.
- **THEN** The reviewing doctor loses treatment-based access.
- **AND** The other doctor's plan remains open.

### Requirement: Require treatment access for doctor operations

Doctors SHALL manage only their own forms.
Plan assignment and review of an owned plan SHALL require an active treatment relationship with the patient.
Review by another doctor SHALL require both an explicit plan access grant and an active treatment relationship.
An appointment alone MUST NOT establish progress access.

#### Scenario: Assign a plan to a treated patient

- **WHEN** A doctor assigns their published form to a patient with an active treatment relationship.
- **THEN** The API permits the assignment subject to the plan's validation rules.

#### Scenario: Reject appointment-only access

- **WHEN** A doctor has an appointment with a patient but no active treatment relationship.
- **THEN** The appointment grants no plan assignment or review access.

#### Scenario: Review another doctor's plan

- **WHEN** A doctor requests another doctor's plan.
- **THEN** Access requires both an explicit grant for that plan and an active treatment relationship with its patient.

### Requirement: Apply resource access consistently

Patients SHALL read their assigned forms and manage entries only for their own permitted plans.
Lists, details, history, summaries, upload requests, finalization, and downloads MUST apply the applicable resource policies.
The API MUST evaluate ownership, treatment access, and plan grants against current application state.

#### Scenario: Restrict collection queries

- **WHEN** An authorized caller requests a list, history, or summary.
- **THEN** The response includes only resources within the caller's permitted scope.

#### Scenario: Conceal absent and unauthorized details

- **WHEN** An authenticated caller with the required role and mapping requests an absent resource or one outside their scope.
- **THEN** The API returns `404`.

#### Scenario: Protect attachment operations

- **WHEN** A caller requests an upload, finalizes an upload, or requests a download URL.
- **THEN** The API authorizes the caller against the associated entry before it grants storage access.

### Requirement: Apply access changes at authorization checks

Local identity disablement and treatment revocation SHALL take effect at the next application authorization check.
Previously issued JWTs and signed URLs can remain valid until expiration.
The documented token lifetime and URL lifetime MUST explain these revocation limits.

#### Scenario: Disable an application identity

- **WHEN** An identity becomes disabled while its Keycloak token remains cryptographically valid.
- **THEN** The next progress authorization check denies access through that mapping.

#### Scenario: Revoke treatment access

- **WHEN** A doctor's treatment relationship becomes inactive.
- **THEN** The next authorization check denies doctor access that requires that relationship.

### Requirement: Preserve stable audit identity

Audit records SHALL identify the mapped caller consistently with the token's issuer and subject.
The migration service's system identity MUST NOT grant doctor or patient HTTP access.

#### Scenario: Audit a raw-claim token

- **WHEN** An authorized request uses a token with raw `iss` and `sub` claims.
- **THEN** Audit records contain the stable mapped caller identifier.

#### Scenario: Reject a migration identity as an HTTP identity

- **WHEN** A request attempts to use the migration service's system identity without the required role and mapping.
- **THEN** The system identity grants no progress endpoint access.

### Requirement: Initialize and preserve local Keycloak state

Local orchestration SHALL provide Keycloak with a stable port, persistent realm and account data, readiness checks, and telemetry.
Initialization MUST create the realm, clients, API roles, and audience mapping without duplicating or discarding existing state.
The API MUST wait for realm initialization and readiness.
Committed configuration MUST contain no passwords or client secrets.

#### Scenario: Initialize a fresh environment

- **WHEN** Local orchestration starts with no existing Keycloak state.
- **THEN** Initialization establishes the required realm, clients, roles, and audience mapping before the API becomes available.
- **AND** Development identities match the seeded doctor and patient mappings.

#### Scenario: Restart without losing identity state

- **WHEN** The application restarts with existing Keycloak storage.
- **THEN** Realm configuration, accounts, administrator credentials, and their mappings remain usable without a volume reset.
- **AND** Realm changes use the documented update procedure rather than an assumption that startup import updates existing realms.

#### Scenario: Observe Keycloak readiness and telemetry

- **WHEN** A developer inspects the local application dashboard.
- **THEN** Keycloak exposes readiness status and its configured telemetry.

### Requirement: Support secure interactive development login

The development Swagger client SHALL use authorization code flow with PKCE and explicit redirect URIs.
Application clients MUST keep password grants and implicit flows disabled.
The progress API MUST NOT collect account passwords or issue its own login or refresh tokens.

#### Scenario: Authorize through Swagger

- **WHEN** A developer signs in through the configured Swagger client.
- **THEN** Keycloak issues an API access token with the required audience and client roles.
- **AND** Swagger can send the token to protected progress endpoints.

### Requirement: Separate production realm initialization from local import

Production configuration SHALL use a durable Keycloak service with explicit realm initialization and an HTTPS authority.
Publication and deployment MUST NOT depend on development-only realm file injection.
The production initialization mechanism remains a separate deployment decision.

#### Scenario: Prepare production configuration

- **WHEN** The application uses production configuration or a production Keycloak connection.
- **THEN** It requires the explicit HTTPS realm authority and a production initialization process independent of local realm import.

### Requirement: Provide local development account credentials

Local orchestration SHALL give the development doctor, patient, and administrator accounts a usable shared password from a local secret.
The secret MUST persist across restarts, and a developer MUST be able to replace it with a chosen value.
These accounts MUST NOT require a password change at first sign-in.
Committed configuration MUST still contain no passwords.

#### Scenario: Sign in on a fresh environment

- **WHEN** Local orchestration starts with no existing Keycloak state and a developer signs in through Swagger as a development account with the local secret password.
- **THEN** Keycloak completes the sign-in without asking for a new password.
- **AND** The resulting access token carries that account's API role.

#### Scenario: Use a developer-chosen password

- **GIVEN** A developer sets the development password secret to a chosen value before the accounts have passwords.
- **WHEN** Local orchestration starts.
- **THEN** Each development account accepts the chosen value as its password.

#### Scenario: Give passwords to existing accounts without one

- **GIVEN** Existing Keycloak state contains development accounts with no password and a pending password update.
- **WHEN** Local orchestration restarts.
- **THEN** Each of those accounts accepts the local secret password and no longer requires a password update.
- **AND** No Keycloak volume reset is required.

#### Scenario: Keep passwords out of committed files

- **WHEN** The repository's realm and orchestration configuration are inspected.
- **THEN** They contain no development account password or password hash.

### Requirement: Preserve existing development account passwords

Initialization MUST NOT replace or remove a password that a development account already has.
Changing the development password secret MUST affect only development accounts without a password.

#### Scenario: Keep a password that a developer changed

- **GIVEN** A developer changed a development account's password in Keycloak.
- **WHEN** Local orchestration restarts.
- **THEN** The account still accepts the developer's password and rejects the local secret password.

#### Scenario: Change the secret after passwords exist

- **GIVEN** The development accounts already have passwords.
- **WHEN** A developer changes the development password secret and restarts local orchestration.
- **THEN** The accounts keep their existing passwords.

### Requirement: Limit development credentials to local development accounts

Only the development accounts defined in the local realm configuration SHALL receive the development password.
Accounts provisioned through the administrator account endpoints MUST keep their existing password process.
Production configuration MUST NOT define or require the development password secret.

#### Scenario: Leave provisioned accounts unchanged

- **GIVEN** An administrator provisioned an account through the account endpoints and it has no password.
- **WHEN** Local orchestration restarts.
- **THEN** The provisioned account still has no password and its required actions are unchanged.

#### Scenario: Publish without development credentials

- **WHEN** The application is published or uses production configuration.
- **THEN** No development password secret is defined or required.
