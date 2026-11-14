# Patient progress tracking: identity and access

**Status:** Implemented.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestone:** 1, with authorization validation across later milestones.
**Identity provider:** The API's own authorization server, built on ASP.NET Core Identity and OpenIddict.
**Decision:** [Use ASP.NET Core Identity and OpenIddict instead of Keycloak](../../docs/adr/20261108-use-aspnet-core-identity-and-openiddict-instead-of-keycloak.md).

## Scope and dependencies

The WebApi authenticates doctors and patients through OpenID Connect and issues access tokens for its own API.
The API validates those tokens and applies application access rules.
PostgreSQL stores sign-in accounts, client registrations, account mappings, and doctor-patient relationships.

Protected endpoints depend on these identity mappings and access policies.
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.
Frontend implementation needs a separate plan.

The operational guide is [`src/WebApi/Features/Identity/README.md`](../../src/WebApi/Features/Identity/README.md).
The behavior contract is `openspec/specs/patient-progress-tracking/identity-and-access/spec.md`.

## Accounts, clients, and roles

ASP.NET Core Identity stores accounts in the application database.
Each account's subject is its ID, formatted as a GUID.
An account must be enabled and have a password to sign in.
Self-registration does not exist.

| Client or scope | Purpose | Requirements |
|---|---|---|
| `medicalapp-api` scope | Represents the protected API and its audience | Access tokens requested with this scope carry the `medicalapp-api` audience and the account's API roles. |
| `medicalapp-web` | Reserved for a future frontend | Authorization code flow with PKCE and explicit redirect URIs. Client type follows the frontend architecture. |
| `medicalapp-swagger` | Supports interactive API testing | Public client, authorization code flow with PKCE, explicit Swagger redirect URI from configuration. |

The migration service registers the scope, the roles, and the configured clients in every environment.
Password, implicit, client credentials, and refresh token flows are disabled.
Only the authorization server's sign-in and password setup pages accept passwords. The API's JSON endpoints never do.

The `doctor`, `patient`, and `administrator` roles are Identity roles.
An account can hold both clinical roles, with the corresponding application mappings.

## API token validation

OpenIddict validation authenticates API calls with the local server's keys.
It is the default authentication scheme, so the sign-in cookie never authorizes API endpoints.

Token validation covers these requirements:

- The signature uses one of the server's configured signing keys.
- The `iss` claim exactly matches `Authentication:Issuer`.
- The `aud` claim includes `medicalapp-api`.
- The token satisfies its expiration and not-before limits, without clock skew.
- The token contains a nonempty `sub` claim.
- Only access tokens are accepted. ID tokens and authorization codes are rejected.

Production requires an explicit HTTPS issuer. Development defaults to `https://localhost:7255/`, and the AppHost sets it to the API's HTTPS endpoint.
Access tokens expire after five minutes and are encrypted.

The API resolves identity from `iss` and `sub`.
It reads API roles only from the `role` claim of a validated access token and accepts only the three role names.
`ApiRoleClaimsTransformation` normalizes those roles for ASP.NET Core authorization policies.

Authentication and authorization middleware run before the FastEndpoints pipeline.
Every progress endpoint requires authentication and the applicable policies.
`ICurrentUserService` returns a stable audit identifier from the mapped identity.

A future server-side ASP.NET Core frontend can sign in through the same authorization server and forward access tokens to this API.
A browser-only frontend uses a public client without a client secret.

## Application identity mappings

The proposed `ApplicationIdentity` record contains a typed ID, issuer, subject, enabled flag, and optional `DoctorId` and `PatientId` links.
PostgreSQL enforces uniqueness for `(Issuer, Subject)`.
Each doctor or patient record links to at most one application identity.
One identity can link to both a doctor and a patient record.

The mapping uses the validated `iss` and `sub` values.
Email addresses, usernames, and display names do not identify account links.
The account subject does not replace `DoctorId` or `PatientId`.

A doctor operation requires the API's `doctor` role and an enabled identity with a doctor mapping.
A patient operation requires the API's `patient` role and an enabled identity with a patient mapping.
An account with a missing or disabled mapping cannot access progress data.

Clients cannot select the acting doctor or patient through request fields or custom identity headers.
A request can identify a target patient or plan, subject to application authorization.

## Account provisioning

The first release uses staff-managed doctor and patient accounts.
Self-registration remains disabled until an approved registration workflow exists.

Administrators provision accounts through the `/api/accounts` endpoints.
Account creation stores the sign-in account and its disabled mapping in one database transaction.
Administrators then link records, assign roles, and enable the mapping. Each step is safe to repeat.
The API does not link accounts through matching usernames or email addresses.
It refuses usernames and emails of accounts that have no mapping.

The account holder chooses a password through a single-use password setup link that an administrator creates.
Links expire after 24 hours by default, and a new link invalidates earlier ones.
The first production administrator comes from the migration service's `bootstrap-admin` command.
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

API roles identify broad capabilities. They do not encode patient lists, plan ownership, or treatment relationships.
The API evaluates these resource rules against current database state.
A request parameter cannot bypass ownership or treatment access.

The [media spec](media-and-storage.md) owns private bucket configuration and temporary signed URLs.
Access policies authorize the request before the API grants access to an object.

## Access changes and response behavior

A local identity disable or treatment relationship revocation takes effect at the next application authorization check.
Disabling an account also ends its sign-in sessions and revokes its pending authorization codes.
Previously issued access tokens can remain valid until expiration after an account disable or role removal.
The five-minute access token lifetime bounds that delay.
Previously issued signed media URLs can remain usable until expiration.

The API uses these response rules:

- Missing, invalid, or expired access tokens return `401`.
- Authenticated accounts without the required role or enabled application mapping receive `403`.
- Resource queries apply access restrictions before they return data.
- Detail requests return `404` for absent resources or resources outside the caller's permitted scope.
- Lists and summaries include only permitted resources.

## Local orchestration

`aspire start` needs no identity container and no manual identity steps.

- The AppHost passes the API's HTTPS endpoint as the issuer to the API and the migration service, and the Swagger redirect URI to the migration service.
- The `dev-account-password` Aspire secret parameter holds the shared development account password. Aspire generates and persists it in AppHost user secrets, and a developer can replace it there.
- The migration service creates the development doctor, patient, and administrator accounts with stable subjects, and sets the password only on accounts without one.
- The migration service maps the development doctor and patient to seeded records, and moves seeded mappings from an earlier issuer to the current one.
- Development signs tokens with persisted development certificates, so tokens survive restarts until they expire.
- Committed files contain no passwords, password hashes, or client secrets.

## Production configuration

Production requires `Authentication:Issuer`, signing and encryption certificates under `Authentication:Certificates`, and persistent data protection keys.
Data protection keys are stored in PostgreSQL, so every instance accepts the cookies, setup links, and codes of the others.
Publication contains no development accounts, development password, development certificates, or development redirect URIs.
Certificate storage, such as Key Vault, remains a deployment decision.

## Implementation map

| Area | Files |
|---|---|
| Accounts and stores | `src/WebApi/Common/Identity/` (`ApplicationUser`, `IdentityStores`, `IdentityAccountAdministration`, `IdentityIssuer`, `ServerCredentials`) |
| Server, validation, cookies, Swagger | `src/WebApi/Host/Extensions/IdentityServerExt.cs` |
| Connect endpoints and pages | `src/WebApi/Features/Identity/Connect/`, `src/WebApi/Features/Identity/Pages/Account/` |
| Role claims | `src/WebApi/Common/Authentication/ApiRoleClaimsTransformation.cs` |
| Provisioning | `src/WebApi/Features/Accounts/` |
| Initialization and bootstrap | `tools/MigrationService/Initializers/`, `tools/MigrationService/AdministratorBootstrap.cs` |
| Tests | `tests/WebApi.IntegrationTests/Authentication/`, `tests/WebApi.IntegrationTests/Authorization/`, `tests/WebApi.IntegrationTests/Common/Infrastructure/Identity/TestIdentity.cs` |

## Validation

Unit tests cover application identity invariants, relationship invariants, role normalization, issuer and certificate configuration, and sign-in rules.
Integration tests use real tokens from the in-process authorization code flow, with the sign-in page, PKCE, and code exchange.
They cover invalid issuers, signatures, audiences, subjects, expiration, signing key rotation, rejected ID tokens, and the sign-in cookie on API endpoints.
They also cover lockout, disabled and passwordless accounts, session expiry, provisioning atomicity and retries, revocation on disable, setup links, initialization, and the administrator bootstrap.
Test authentication handlers do not replace these checks.

## Open decisions

The remaining decisions are:

- Production certificate storage and data protection key encryption at rest
- Per-IP rate limiting for sign-in and token requests
- Production deployment, operator access, and backup procedures.
