# identity-and-access Specification

## Purpose

Authenticate doctors, patients, and administrators through the application's own authorization server and restrict progress data to callers with valid application mappings and resource access.

## Requirements

### Requirement: Validate API access tokens

The API SHALL accept only access tokens that the application's own authorization server issued, with a trusted signature, the configured issuer, the `medicalapp-api` audience, valid time limits, and a nonempty subject.
Every progress endpoint MUST require authentication.
ID tokens, authorization codes, and sign-in cookies MUST NOT authorize API requests.

#### Scenario: Accept a valid access token

- **WHEN** A caller presents an access token that the authorization server issued with the configured issuer, the required audience, a subject, and valid time limits.
- **THEN** The API authenticates the caller and evaluates application authorization.

#### Scenario: Reject invalid authentication

- **WHEN** A request has no token, an invalid signature or issuer, a missing audience or subject, invalid time limits, or an ID token.
- **THEN** The API returns `401` and exposes no progress data.

#### Scenario: Accept a rotated signing key

- **GIVEN** The authorization server is configured with a new signing key while the previous key is still configured for validation.
- **WHEN** A caller presents an unexpired access token signed with either key.
- **THEN** The API accepts the token.
- **AND** New tokens are signed with the new key.

### Requirement: Preserve production transport and issuer validation

Production SHALL require an explicitly configured HTTPS issuer URL.
The issuer MUST be an absolute HTTPS URL without query, fragment, or user information, and tokens MUST carry exactly that issuer.
Local HTTP endpoints and a derived issuer MUST require explicit development configuration.

#### Scenario: Validate production configuration

- **WHEN** Production starts without an explicit issuer, or with an issuer that is not a valid HTTPS URL.
- **THEN** Startup rejects the configuration.

#### Scenario: Use a consistent local issuer

- **WHEN** A developer obtains a token through the browser and calls the locally orchestrated API.
- **THEN** Browser sign-in, token issuance, API validation, and the seeded development mappings agree on the canonical issuer.
- **AND** Host and container addresses do not require an issuer validation exception.

### Requirement: Scope roles to the API client

The API SHALL read doctor, patient, and administrator roles only from the role claim of a validated access token issued for the `medicalapp-api` audience.
One account can hold both roles.
Role values outside the defined API roles, and role claims in ID tokens or other token types, MUST NOT grant application permissions.

#### Scenario: Ignore unrelated roles

- **WHEN** A valid access token carries role values other than `doctor`, `patient`, or `administrator`.
- **THEN** Those values grant no doctor, patient, or administrator permissions.

#### Scenario: Support an account with both roles

- **WHEN** A caller has both API roles and enabled doctor and patient mappings.
- **THEN** The caller can use each role within its corresponding resource scope.

#### Scenario: Require the administrator API role

- **WHEN** A caller requests an administrator operation without the administrator API role.
- **THEN** The API denies the operation and changes no account, treatment relationship, or grant.

#### Scenario: Reflect a role change in new tokens

- **WHEN** An administrator changes an account's API roles.
- **THEN** Access tokens issued after the change carry the new roles.
- **AND** Tokens issued before the change keep their roles until they expire.

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

The API SHALL provide administrator endpoints for account creation, linking, role assignment, password setup links, disablement, and recovery.
Only designated administrators with the administrator API role MUST use these endpoints.

#### Scenario: Reject unauthorized provisioning

- **WHEN** A caller without administrator authorization requests an account operation.
- **THEN** The API changes no account, role, mapping, or password setup link.

### Requirement: Provision accounts through trusted staff operations

The first release SHALL use staff-managed accounts with self-registration disabled.
Provisioning MUST support retries without duplicate accounts, mappings, or domain records.
Creating an account MUST create the sign-in account and its disabled mapping together, or neither.
Mappings MUST remain disabled until all required provisioning steps finish.
Doctor and patient request payloads MUST NOT assign roles or change account mappings.

#### Scenario: Create an account atomically

- **WHEN** Account creation fails after the sign-in account would have been stored.
- **THEN** Neither the sign-in account nor the mapping exists.
- **AND** A retry with the same username creates both.

#### Scenario: Retry account creation

- **WHEN** An administrator repeats account creation with a username that provisioning already created.
- **THEN** The API returns the existing mapping and creates no second account or mapping.

#### Scenario: Refuse an account that provisioning did not create

- **WHEN** An administrator requests account creation with the username or email of an account that has no application mapping.
- **THEN** The API returns `409` and links nothing.

#### Scenario: Recover incomplete provisioning

- **WHEN** An account exists but its links or roles are incomplete.
- **THEN** The mapping grants no progress access.
- **AND** The account details list the remaining steps, and repeating them finishes provisioning without duplicate identities or domain records.

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
Previously issued access tokens and signed URLs can remain valid until expiration.
The documented token lifetime and URL lifetime MUST explain these revocation limits.

#### Scenario: Disable an application identity

- **WHEN** An identity becomes disabled while an access token issued for it remains cryptographically valid.
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

### Requirement: Support secure interactive development login

The development Swagger client SHALL use authorization code flow with PKCE and explicit redirect URIs.
The authorization server MUST reject password grants, implicit flows, and authorization requests without PKCE.
The API's JSON endpoints MUST NOT accept account passwords. Only the authorization server's sign-in and password setup pages accept them.

#### Scenario: Authorize through Swagger

- **WHEN** A developer signs in through the configured Swagger client.
- **THEN** The authorization server issues an API access token with the required audience and roles.
- **AND** Swagger can send the token to protected progress endpoints.

#### Scenario: Reject an unregistered redirect URI

- **WHEN** An authorization request names a redirect URI that is not registered for the client.
- **THEN** The authorization server rejects the request and issues no code.

#### Scenario: Reject a password grant

- **WHEN** A client requests a token with the password grant type.
- **THEN** The token endpoint rejects the request.

### Requirement: Provide local development account credentials

Local orchestration SHALL give the development doctor, patient, and administrator accounts a usable shared password from a local secret.
The secret MUST persist across restarts, and a developer MUST be able to replace it with a chosen value.
These accounts MUST NOT require a password change at first sign-in.
Committed configuration MUST still contain no passwords.

#### Scenario: Sign in on a fresh environment

- **WHEN** Local orchestration starts with an empty database and a developer signs in through Swagger as a development account with the local secret password.
- **THEN** The authorization server completes the sign-in without asking for a new password.
- **AND** The resulting access token carries that account's API role.

#### Scenario: Use a developer-chosen password

- **GIVEN** A developer sets the development password secret to a chosen value before the accounts have passwords.
- **WHEN** Local orchestration starts.
- **THEN** Each development account accepts the chosen value as its password.

#### Scenario: Give passwords to existing accounts without one

- **GIVEN** The database contains development accounts with no password.
- **WHEN** Local orchestration restarts.
- **THEN** Each of those accounts accepts the local secret password.
- **AND** No database reset is required.

#### Scenario: Keep passwords out of committed files

- **WHEN** The repository's seed and orchestration configuration are inspected.
- **THEN** They contain no development account password or password hash.

### Requirement: Preserve existing development account passwords

Initialization MUST NOT replace or remove a password that a development account already has.
Changing the development password secret MUST affect only development accounts without a password.

#### Scenario: Keep a password that a developer changed

- **GIVEN** A developer changed a development account's password through a password setup link.
- **WHEN** Local orchestration restarts.
- **THEN** The account still accepts the developer's password and rejects the local secret password.

#### Scenario: Change the secret after passwords exist

- **GIVEN** The development accounts already have passwords.
- **WHEN** A developer changes the development password secret and restarts local orchestration.
- **THEN** The accounts keep their existing passwords.

### Requirement: Limit development credentials to local development accounts

Only the seeded development doctor, patient, and administrator accounts SHALL receive the development password.
Accounts provisioned through the administrator account endpoints MUST get their password only through a password setup link.
Production configuration MUST NOT define or require the development password secret.

#### Scenario: Leave provisioned accounts unchanged

- **GIVEN** An administrator provisioned an account through the account endpoints and it has no password.
- **WHEN** Local orchestration restarts.
- **THEN** The provisioned account still has no password.

#### Scenario: Publish without development credentials

- **WHEN** The application is published or uses production configuration.
- **THEN** No development password secret is defined or required, and no development account is created.

### Requirement: Sign in through the authorization server

The authorization server SHALL authenticate accounts with a username and password on its own sign-in page, then continue the pending authorization request.
It MUST offer no self-registration.
Failed attempts MUST return the same message whether the username exists or not.
Repeated failures MUST lock the account temporarily.

#### Scenario: Sign in and continue authorization

- **WHEN** An enabled account with a password signs in from an authorization request.
- **THEN** The browser returns to the client's registered redirect URI with an authorization code.

#### Scenario: Reject wrong credentials without revealing accounts

- **WHEN** A sign-in uses an unknown username, or a known username with a wrong password.
- **THEN** The page shows the same failure message and issues no code.

#### Scenario: Lock out repeated failures

- **WHEN** An account reaches the configured number of consecutive failed sign-ins.
- **THEN** Sign-in for that account fails, even with the correct password, until the lockout period ends.

#### Scenario: Refuse a disabled or passwordless account

- **WHEN** A disabled account, or an account without a password, attempts to sign in.
- **THEN** Sign-in fails and no code is issued.

#### Scenario: Offer no registration

- **WHEN** A visitor looks for a way to create an account on the authorization server.
- **THEN** No registration page or endpoint exists.

### Requirement: Keep sign-in sessions off the API

The authorization server's sign-in session SHALL be used only by its sign-in, password setup, authorize, and end-session endpoints.
API endpoints MUST ignore sign-in cookies.
The sign-in session MUST expire after 30 minutes of inactivity and after 8 hours in total.

#### Scenario: Ignore a sign-in cookie on the API

- **WHEN** A browser with a valid sign-in session calls a progress endpoint without an access token.
- **THEN** The API returns `401`.

#### Scenario: Expire an idle sign-in session

- **WHEN** A sign-in session has been idle for more than 30 minutes and the client starts a new authorization request.
- **THEN** The authorization server asks for the password again.

#### Scenario: Sign out

- **WHEN** A signed-in user follows the end-session flow.
- **THEN** The sign-in session ends, and the next authorization request asks for the password.

### Requirement: Keep access tokens short-lived

Access tokens SHALL expire five minutes after issue, and the API MUST validate expiration without clock skew.
The authorization server MUST NOT issue refresh tokens to the application clients.

#### Scenario: Reject an expired access token

- **WHEN** A caller presents an access token after its expiration time.
- **THEN** The API returns `401`.

#### Scenario: Refuse offline access

- **WHEN** A client requests a refresh token or the `offline_access` scope.
- **THEN** The authorization server issues no refresh token.

### Requirement: Issue password setup links

The API SHALL provide an administrator endpoint that returns a single-use password setup link for a provisioned account.
The link MUST expire after a configured period, 24 hours by default.
Issuing a new link MUST invalidate earlier unused links for that account.
The response MUST NOT contain or reveal a password.

#### Scenario: Issue a link for a new account

- **WHEN** An administrator requests a password setup link for a provisioned account.
- **THEN** The API returns the link and its expiration time.

#### Scenario: Replace an earlier link

- **GIVEN** An administrator issued a password setup link for an account.
- **WHEN** The administrator issues another link for the same account.
- **THEN** Only the newest link can set a password.

#### Scenario: Refuse a link for an unprovisioned account

- **WHEN** An administrator requests a password setup link for a mapping that does not exist.
- **THEN** The API returns `404`.

### Requirement: Set a password through a setup link

The authorization server SHALL let the holder of a valid password setup link choose the account's password.
The password MUST meet the configured password policy, at least 12 characters by default.
A link MUST stop working once it is used or expires.
Setting a password MUST end the account's other sign-in sessions.

#### Scenario: Set a password with a valid link

- **WHEN** The holder opens a valid link and submits a password that meets the policy.
- **THEN** The account accepts that password at sign-in.

#### Scenario: Reject a reused or expired link

- **WHEN** Someone submits a password through a link that was already used, replaced, or expired.
- **THEN** The password is not changed and the page asks the holder to request a new link from an administrator.

#### Scenario: Reject a weak password

- **WHEN** The holder submits a password that does not meet the policy.
- **THEN** The password is not changed, the link stays usable, and the page explains the policy.

### Requirement: End sessions and revoke credentials when disabling an account

Disabling an account SHALL disable its mapping first, then disable sign-in for the account.
It MUST end the account's sign-in sessions and revoke its outstanding authorization codes and server-stored tokens.
Enabling the account again MUST NOT restore revoked credentials.

#### Scenario: Stop new tokens after disable

- **GIVEN** An account has an active sign-in session and an unused authorization code.
- **WHEN** An administrator disables the account.
- **THEN** The authorization code can no longer be exchanged, and a new authorization request requires a sign-in that fails.

#### Scenario: Repeat a disable

- **WHEN** An administrator disables an account that is already disabled.
- **THEN** The operation succeeds and changes nothing further.

### Requirement: Register clients and scopes through trusted initialization

Database initialization SHALL create or update the `medicalapp-api` scope and resource, the API roles, and the configured application clients in every environment.
Initialization MUST leave unrelated clients, accounts, and role assignments unchanged.
Client redirect URIs MUST come from configuration, and production MUST NOT receive development redirect URIs by default.

#### Scenario: Initialize a fresh database

- **WHEN** Initialization runs against an empty database.
- **THEN** The scope, resource, roles, and configured clients exist before the API accepts sign-ins.

#### Scenario: Reconcile a changed client

- **WHEN** A configured client's redirect URIs change and initialization runs again.
- **THEN** The client uses the new redirect URIs, and no duplicate client exists.

### Requirement: Initialize and preserve local identity state

Local orchestration SHALL seed the development doctor, patient, and administrator accounts with stable subjects and roles, and link the doctor and patient to the seeded records.
Accounts, passwords, client registrations, signing keys, and mappings MUST survive restarts.
Seeded mappings from an earlier issuer MUST move to the current issuer.

#### Scenario: Initialize a fresh environment

- **WHEN** Local orchestration starts with an empty database.
- **THEN** The development accounts exist with their roles, and the doctor and patient accounts map to the seeded records under the current issuer before the API becomes available.

#### Scenario: Restart without losing identity state

- **WHEN** The application restarts with an existing database.
- **THEN** Accounts, passwords, roles, mappings, and client registrations remain usable.
- **AND** Access tokens issued before the restart remain valid until they expire.

#### Scenario: Move seeded mappings from Keycloak

- **GIVEN** The database contains the seeded development mappings with the former Keycloak issuer.
- **WHEN** Local orchestration starts.
- **THEN** Those mappings use the current issuer and keep their doctor and patient links and their enabled state.

#### Scenario: Keep a deliberately disabled seeded mapping

- **GIVEN** A developer disabled a seeded development mapping.
- **WHEN** Local orchestration restarts.
- **THEN** The mapping stays disabled.

### Requirement: Require durable production identity configuration

Production SHALL require configured signing and encryption certificates and a persistent data protection key store shared by all instances.
Startup MUST fail when any of them is missing.
Development and test keys MUST NOT be used outside the Development and test environments.

#### Scenario: Reject missing production keys

- **WHEN** Production starts without signing or encryption certificates.
- **THEN** Startup fails with a message that names the missing setting.

#### Scenario: Share sign-in state across instances

- **WHEN** Two production instances serve one sign-in flow.
- **THEN** Each instance accepts the sign-in session, password setup link, and authorization code that the other created.

#### Scenario: Publish without local identity resources

- **WHEN** The application is published.
- **THEN** The publication contains no development accounts, development password, development certificates, or development redirect URIs.

### Requirement: Bootstrap the first administrator

Operators SHALL be able to create an administrator account through a trusted command run against the target database.
The command MUST refuse to run when an enabled administrator account already exists, unless the operator forces it.
It MUST print a password setup link only to the operator's console and MUST NOT write the link to logs or telemetry.

#### Scenario: Create the first administrator

- **WHEN** An operator runs the bootstrap command with a username and email on a database without an administrator.
- **THEN** An administrator account exists and the console shows its password setup link.

#### Scenario: Refuse a second bootstrap

- **WHEN** An operator runs the bootstrap command and an enabled administrator already exists.
- **THEN** The command creates nothing and exits with an error unless the operator forces it.
