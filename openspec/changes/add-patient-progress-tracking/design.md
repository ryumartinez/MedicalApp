# Design

## Context

See [proposal.md](proposal.md) for the motivation and release scope.
The user confirmed that none of the imported progress features are implemented.
Read-only inspection confirms that the repository has doctor, patient, and appointment entities but no progress domains, Keycloak configuration, or Garage resources.

The AppHost currently starts PostgreSQL 17, runs `tools/MigrationService`, and starts the API after migrations finish.
Its SDK and central Aspire package versions are `13.4.3`.
The solution targets .NET 10 and uses EF Core with PostgreSQL.
The existing Azure publication configuration must remain compatible with local-only identity and storage initialization.

`Program.cs` has no authentication registration or middleware.
`CurrentUserService` reads `ClaimTypes.NameIdentifier`.
The migration service has a separate system identity and registers `TimeProvider.System`.
Integration tests already use real PostgreSQL through Testcontainers and Respawn.
Their web factory currently configures only the database connection.

## Goals / Non-Goals

**Goals:**

- Preserve the source proposal as an active, unimplemented OpenSpec change.
- Keep capability behavior in delta specs and implementation choices in this design.
- Extend the existing vertical slices, persistence, migration service, and test fixtures.
- Define consistent transaction boundaries for closure, submission, asset attachment, and cleanup.
- Preserve all existing open decisions without inventing limits or deployment choices.

**Non-Goals:**

- Implement application features during this conversion.
- Establish an implemented baseline before implementation and verification finish.
- Replace the repository architecture or its existing appointment behavior.
- Design production hosting, backups, or a frontend beyond the source proposal's constraints.

## Decisions

### 1. Import one release change with six capability boundaries

Use `add-patient-progress-tracking` as one active change because the source documents define one first release with shared dependencies.
Preserve their existing capability names under `patient-progress-tracking/`.
All delta requirements use `ADDED` because the OpenSpec baseline is empty.
Every behavior has at least one acceptance scenario.

The alternatives are an immediate baseline import or separate changes for each milestone.
An immediate baseline import obscures the unimplemented status.
Separate changes require additional coordination for the shared submission contract and release acceptance.
Splitting the change later remains possible if the release scope changes.

The original documents remain available as provenance.
The OpenSpec artifacts own future planning updates for this change.
Compare source details through this map:

| Source document | OpenSpec destination |
|---|---|
| [Release plan](../../../specs/patient-progress-tracking.md) | Proposal, shared design constraints, four task milestones, and release validation |
| [Identity and access](../../../specs/patient-progress-tracking/identity-and-access.md) | [Identity delta](specs/patient-progress-tracking/identity-and-access/spec.md), identity design, and milestone 1 tasks |
| [Forms and publishing](../../../specs/patient-progress-tracking/forms-and-publishing.md) | [Forms delta](specs/patient-progress-tracking/forms-and-publishing/spec.md), form design, and milestone 2 tasks |
| [Plans and scheduling](../../../specs/patient-progress-tracking/plans-and-scheduling.md) | [Plans delta](specs/patient-progress-tracking/plans-and-scheduling/spec.md), schedule design, and milestones 1 and 3 tasks |
| [Entries and submission](../../../specs/patient-progress-tracking/entries-and-submission.md) | [Entries delta](specs/patient-progress-tracking/entries-and-submission/spec.md), transaction design, and milestone 3 tasks |
| [Media and storage](../../../specs/patient-progress-tracking/media-and-storage.md) | [Media delta](specs/patient-progress-tracking/media-and-storage/spec.md), storage design, and milestones 1 and 3 tasks |
| [History and summaries](../../../specs/patient-progress-tracking/history-and-summaries.md) | [History delta](specs/patient-progress-tracking/history-and-summaries/spec.md) and milestone 4 tasks |

Baseline specs belong under `openspec/specs/patient-progress-tracking/<capability>/spec.md` after the implemented change is verified and archived.
This conversion neither syncs the deltas nor archives the change.
Unchecked tasks describe future implementation, not the completed document conversion.

### 2. Preserve repository conventions and aggregate boundaries

Use the existing `CreateHero` slice as the endpoint shape.
Each use case contains separate endpoint, request, applicable response, validator, and Swagger summary files.
Every endpoint belongs to its feature group and has an explicit OpenAPI operation name.
Feature-specific DI is necessary only for feature services.

Domain objects use static factories, private EF constructors, property guards, and entity constants for field limits.
Use `Entity<TId>` unless an aggregate raises domain events and requires `AggregateRoot<TId>`.
Declare Vogen IDs with `Guid.CreateVersion7()` and register every ID in `VogenEfCoreConverters.cs`.
Related aggregates refer to typed IDs.
Namespaces mirror folders.
Aggregate query specifications use static factory methods.

EF configurations define relationships, field limits, unique indexes, and concurrency tokens.
Add DbSets through partial `ApplicationDbContext` files in the corresponding persistence area.
Generate migrations through the existing WebApi project and apply them through `MigrationService`.
Register `TimeProvider` for API time calculations.
Return `409` for conflicting updates without breaking the submission retry contract.

Use these feature boundaries:

| Domain and persistence area | Feature group and use cases |
|---|---|
| `ProgressForms/` | `ProgressFormsGroup`: `CreateProgressForm`, `UpdateProgressFormDraft`, `DuplicateProgressForm`, `PublishProgressForm`, `GetProgressForm`, `GetProgressForms` |
| `ProgressPlans/` | `ProgressPlansGroup`: `AssignProgressPlan`, `EndProgressPlan`, `GetProgressPlan`, `GetPatientProgressPlans` |
| `ProgressEntries/` | `ProgressEntriesGroup`: `GetDueProgressEntries`, `GetProgressEntry`, `SaveProgressEntryDraft`, `SubmitProgressEntry`, `GetProgressHistory`, `GetProgressSummary` |
| `MediaAssets/` | `MediaAssetsGroup`: `RequestMediaUpload`, `FinalizeMediaUpload`, `GetMediaDownloadUrl` |

Domain areas live under `src/WebApi/Common/Domain/`.
Persistence areas live under `src/WebApi/Common/Persistence/`.
Groups and use cases live under `src/WebApi/Features/`.
Shared identity infrastructure follows the source identity document's file responsibilities rather than an artificial feature slice.

### 3. Use the selected official Keycloak integrations

Keycloak is a source decision, not a new provider selection.
Use the official [Aspire Keycloak integration](https://aspire.dev/integrations/security/keycloak/) for hosting and API authentication.
Pin compatible `Aspire.Hosting.Keycloak` and `Aspire.Keycloak.Authentication` versions in `Directory.Packages.props`.
Reference them from the AppHost and WebApi respectively.
The source identifies these integrations as preview.
Validate their current APIs and pin a documented `quay.io/keycloak/keycloak` image version during milestone 1.

Retain these proposed first-release defaults:

| Item | Proposed value |
|---|---|
| Realm | `medicalapp` |
| API client and audience | `medicalapp-api` |
| API client roles | `doctor`, `patient` |
| Development Swagger client | `medicalapp-swagger`, public client with authorization code flow and PKCE |
| Future frontend client | `medicalapp-web`, configured by the separate frontend plan |
| Stable local port | `8080` |

Use `AddKeycloak`, `WithDataVolume`, `WithRealmImport("./Keycloak/Realms")`, and `WithOtlpExporter` in local orchestration.
The API references the resource and waits for readiness and completed realm initialization.
Keep realm files under `tools/AppHost/Keycloak/Realms/`.
The data volume persists `/opt/keycloak/data`.
Avoid host-specific bind mounts for this state.

Use Aspire secret parameters or local user secrets for credentials.
The generated administrator password uses `Parameters:keycloak-password`.
Changing bootstrap parameters does not reset credentials already in the volume.
Volume resets remain explicit operator actions.
Committed realm files contain no account passwords or client secrets.

Startup import initializes an absent realm but does not update an existing one.
Document an explicit reconciliation or migration procedure and make repeated initialization preserve accounts and realm state.
Development subjects must match the seeded doctor and patient identity mappings.
The management readiness check uses `/health/ready`.
Process readiness alone does not prove that realm and clients exist.

The required API registration remains:

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

Validate the production authority as an explicit HTTPS URL for `/realms/medicalapp`.
Aspire's `https+http://` discovery scheme does not satisfy this requirement.
Normalize roles only from `resource_access.medicalapp-api.roles` after token validation.
Run authentication and authorization middleware before FastEndpoints.
Align audit identity with raw `iss` and `sub` claims instead of assuming `ClaimTypes.NameIdentifier` exists.

Add the source identity files in these areas:

| Area | Responsibilities |
|---|---|
| `Host/Extensions/KeycloakAuthenticationExt.cs` | JWT registration, authority validation, authorization registration, and Swagger security |
| `Common/Authentication/KeycloakRoleClaimsTransformation.cs` | Validated API role normalization |
| `Common/Authorization/` | `ProgressPolicies` and shared `ProgressAuthorizationService` |
| `Common/Interfaces/` and `Common/Services/` | Current identity resolution and shared authorization contracts |
| `Common/Domain/ApplicationIdentities/` | Identity mapping, typed ID, invariants, errors, and query specifications |
| `Common/Domain/DoctorPatientRelationships/` | Treatment relationships, typed ID, invariants, errors, and query specifications |
| `Common/Persistence/Identity/` | DbSets, mappings, uniqueness, relationships, and migrations |
| `tools/AppHost/Keycloak/` | Realm configuration and operator instructions |
| `tools/MigrationService/Initializers/ProgressIdentityInitializer.cs` | Idempotent development identity and treatment seed data |

Store `(Issuer, Subject)` uniquely and enforce at most one identity per linked doctor or patient record.
Require enabled role-specific mappings as well as the corresponding API role.
Persist explicit treatment relationships and plan access grants.
Appointments and matching email addresses do not establish access.
Apply database-backed access restrictions to queries before returning data.
Use the specified `401`, `403`, and scoped `404` response behavior consistently.

Staff-managed provisioning remains the proposed workflow.
The operator procedure must cover creation, role assignment, linking, disabling, and recovery.
Keep mappings disabled until provisioning finishes because Keycloak and PostgreSQL have no shared transaction.
Patients and doctors cannot provision their own roles or mappings through progress payloads.

Production cannot depend on `WithRealmImport` development file injection.
Preserve the existing publication path with local initialization guarded from publish and deploy operations.
An external production identity service can use `AddConnectionString("keycloak")` with an explicit HTTPS realm authority.
A custom image can retain `AddKeycloak` through `WithDockerfile`.
Production realm initialization, durable database, backups, TLS, and external hostname remain deployment decisions.
`AddKeycloakOpenIdConnect` and frontend cookie handling belong to the separate frontend plan.

### 4. Keep published definitions separate from editable drafts

`ProgressForm` owns its draft and published versions.
`ProgressFormVersion` records a unique version number, publication time, and immutable questions.
`ProgressQuestion` carries a stable cross-version key and a version-specific identifier.
Choice options have stable identifiers within the published definition.
Numeric questions preserve bounds, optional steps, and endpoint labels.
Image and video questions each accept one file.

`ProgressPlan` stores typed doctor, patient, and published version references, schedule, IANA timezone, local dates, and ended timestamp.
Replace an assignment with a new plan rather than changing its historical definition.
`ProgressEntry` owns typed answers, period identity, UTC boundaries, status, and actual submission time.
`ProgressAnswer` stores exactly one applicable value representation, including an asset reference for attachment answers.

A mutable shared definition is an alternative, but it changes the interpretation of historical answers.
Immutable versions keep validation and reporting tied to the definition the patient received.

### 5. Calculate schedule periods through local calendar rules

Use local dates and an IANA timezone to calculate periods.
Daily periods cover one local date.
Weekly periods cover seven local dates anchored to the plan's start date.
An inclusive end date can shorten the last period.
Convert each boundary to UTC through timezone rules rather than adding fixed hour counts.

Use `TimeProvider` for eligibility, due states, missed periods, and actual submission time.
Do not create empty entries through a scheduler.
Calculate due and missed periods from the schedule and existing entries.
Use the same calculation for reporting and submission validation.
Preserve the source's unresolved transition policy explicitly in the decision register.

### 6. Treat submission as the owner of the shared transaction contract

PostgreSQL enforces uniqueness for `(ProgressPlanId, PeriodStartDate)`.
Resolve concurrent draft creation without persisting duplicate entries.
Use optimistic concurrency for conflicting form, plan, and entry changes.
An already committed submission returns its existing result on an authorized retry without another write.

The final submission transaction must validate the still-active plan, saved draft, assigned version, and every referenced asset.
It commits the entry's submitted state and the assets' attached states together.
Closure and cleanup must participate in a common database coordination protocol.
Only submission can move ready assets to attached.

A proposed implementation uses a consistent lock order: plan, entry, then media assets ordered by identifier.
Plan closure coordinates on the same plan row as draft changes and new submissions.
Cleanup claims eligible asset state through database coordination before it deletes storage objects.
A committed attachment prevents a cleanup claim.
A committed cleanup claim prevents submission from accepting that asset.
Validate this protocol through real PostgreSQL races before accepting it.
This lock protocol is an implementation proposal added during conversion, not an already approved source default.

Application-only eligibility checks are an alternative, but state can change between validation and commit.
PostgreSQL transaction coordination closes that gap.
Do not place network calls to Garage inside the final submission transaction.
Files must already be validated and ready.

### 7. Keep storage infrastructure outside the domain

`MediaAsset` belongs under its own domain and persistence areas.
It records the owner, entry, question, key, content type, size, state, and expiration.
Use `IObjectStorage` under `Common/Interfaces/` and its Garage implementation under `Common/Storage/Garage/`.
Register `AWSSDK.S3` with Garage's endpoint and path-style addressing.
Domain types have no Garage or S3 SDK dependency.

The AppHost owns a custom Garage resource and initialization resources under `tools/AppHost/Garage/`.
Pin and validate the Garage image and configuration during milestone 1.
Persist both metadata and object data.
Initialize node layout, private bucket, and scoped access key idempotently.
The API waits for initialization completion and storage readiness.
Keep the administrative endpoint internal and credentials in secret parameters or local user secrets.

Upload URLs target staging keys generated by the API.
Finalization validates ownership, object existence, size, expiration, and actual format.
Promote exactly the validated content to a separate final key before marking the asset ready.
The selected Garage primitive must preserve content identity despite concurrent staging replacement.
Validate a version-specific operation, a conditional operation, or a private snapshot against the selected Garage version before choosing the mechanism.
Do not assume that the full AWS S3 feature set exists in Garage.

A shared staging and final key is an alternative, but a valid upload URL can overwrite finalized content.
Separate keys protect final files from client upload capabilities.
Finalization retries return the existing result.
Failures leave the asset unavailable for submission and permit recovery without duplicate logical assets.

Cleanup runs through a scheduled hosted service with database coordination and retryable object removal.
It removes expired staging objects and eligible unattached final objects while preserving attached media.
Browser URLs use a reachable S3 endpoint and the required CORS configuration.
Production Garage durability, deployment, and backups require a separate deployment plan.

### 8. Reuse reporting and validation infrastructure

Keep history and summaries in the `ProgressEntries` feature group.
Use aggregate specifications with static factory methods for nontrivial queries.
Return attachment identifiers and metadata rather than public object access.
Authorize download URL requests separately.
Keep question and version identifiers in numeric trends so changed scales remain distinguishable.

Keep domain invariant tests in `WebApi.UnitTests`.
Extend `WebApi.IntegrationTests` with real PostgreSQL constraints, endpoint authorization, and transaction races.
Add real Keycloak tokens and Garage objects through dedicated integration fixtures.
Test authentication handlers do not replace Keycloak issuer, audience, signature, role, rotation, and ID-token checks.
Retain architecture tests for naming and layering.

Development seed data demonstrates daily mood and energy, weekly structured therapy, and weekly exercise videos.
Release validation includes the build, all three test projects, pending-model-change checks, and Aspire smoke checks.
Restart checks cover Keycloak accounts and Garage metadata and bytes.

### 9. Preserve the source decision register

The conversion does not resolve product or deployment choices that the original plans left open.
The table records implementation checkpoints, not permission to choose arbitrary defaults.
Resolve behavior-affecting choices with the user before the affected implementation and update the corresponding artifacts explicitly.

| Checkpoint | Imported decision | Effect on future work |
|---|---|---|
| Milestone 1 identity | Accept or revise staff-managed provisioning, select the trusted tool, and define staff permissions | Finalize provisioning operations and the operator procedure |
| Milestone 1 access | Define treatment creation, revocation, and plan grant management | Finalize trusted management paths and persisted grants |
| Before plan closure behavior | Decide whether treatment revocation also ends existing patient plans | Keep access revocation distinct from an unspecified automatic closure rule |
| Milestone 1 tokens | Select access token lifetime, refresh session policy, and permitted revocation delay | Configure and document the delay for already issued tokens |
| Milestone 1 dependencies | Select compatible Keycloak packages and image, plus Garage image and configuration | Pin validated versions without assuming source examples match current APIs |
| Milestone 1 scheduling | Define deterministic ambiguous and invalid local boundary handling | Add explicit timezone boundary examples before schedule implementation |
| Milestone 2 forms | Select form size, question count, text lengths, choice count, and option label limits | Define shared constants, validation, and persistence limits |
| Milestone 3 entries | Select answer text limits and submitted entry retention | Define validation and retention without weakening submitted immutability |
| Before milestone 3 uploads | Select formats, file size limits, expiration, cleanup timing, media retention, and quotas | Configure and validate the upload and cleanup rules |
| Separate production deployment | Select Keycloak database and realm initialization, operator access, durable storage, deployment, and backups | Add deployment files and procedures in that separate scope |
| Separate later releases | Decide reminders, feedback, corrections, conditional questions, and advanced reports | Preserve first-release boundaries |

The original schedule and submission rules remain proposed defaults.
Revisions must remain explicit rather than appearing only in code.
Provisioning tool files and deployment entry points join the implementation file map after their relevant decisions.

## Risks / Trade-offs

- Preview identity integrations can differ from source examples → Validate official APIs and pin compatible package and image versions.
- Realm startup import does not update existing realms → Document and validate explicit reconciliation without resetting persistent state.
- Browser and container addresses can disagree on the issuer or storage endpoint → Validate canonical URLs through Swagger and browser upload smoke checks.
- UTC duration shortcuts can misclassify periods → Calculate through local dates and validate daylight saving boundaries.
- Submission, closure, and cleanup can race → Validate their shared database protocol with concurrent PostgreSQL integration tests.
- Staging replacement can substitute unchecked bytes → Validate content-preserving promotion against the pinned Garage version.
- Database and object storage commits are independent → Keep failed assets unavailable and make finalization and cleanup recovery retryable.
- One release change spans several domains → Keep milestone completion criteria explicit and all unfinished tasks unchecked.
- Retained source documents can diverge → Maintain planning updates in this active change and use source documents only for provenance.
- Token and URL revocation is delayed until expiration → Document lifetimes and enforce local mapping and relationship changes at each authorization check.

## Migration Plan

The document conversion adds only this active change and its planning artifacts.
It leaves source documents, application code, baseline specs, and unrelated working-tree changes untouched.

Future implementation follows the four source milestones:

1. Establish identity, storage, typed IDs, and schedule foundations before protected progress endpoints or upload workflows.
2. Add form persistence, editable drafts, duplication, and immutable publication.
3. Add assignments, entries, signed uploads, validated finalization, atomic submission, cleanup, downloads, and plan closure.
4. Add history, summaries, seed scenarios, Swagger documentation, and release validation.

Each persistence change uses a generated EF migration through the existing migration service.
Development initializers remain idempotent and run only in development after schema creation.
Keep production initialization separate from development realm import and local Garage setup.

Before release, validate migrations and the source's complete acceptance scenarios against real services.
Rollback requires a reviewed data-preserving plan once clinical entries or attachments exist.
Do not reset persistent volumes or erase submitted records as a rollback shortcut.
Production rollback and backup procedures belong to the separate deployment plan.

After implementation and validation finish, archive the change and merge its deltas into the baseline.
Do not mark tasks completed merely because these artifacts exist.
