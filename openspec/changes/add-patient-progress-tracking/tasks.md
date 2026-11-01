# Tasks

These tasks describe future implementation of the imported proposal.
All tasks remain unchecked because the features are unimplemented.
Resolve the design's imported decision checkpoints before their affected work.
The original four milestones determine the order of these groups.

## 1. Milestone 1 — Identity and access

- [ ] 1.1 Pin compatible official Aspire Keycloak packages and a documented image version. Validate package restore and AppHost/WebApi builds.
- [ ] 1.2 Record the agreed provisioning workflow, trusted tool, and staff permissions. Deliver the decision entry before provisioning implementation.
- [ ] 1.3 Record treatment and plan grant management rules, including whether treatment revocation closes plans. Deliver explicit access and closure examples.
- [ ] 1.4 Record token lifetime, refresh session policy, and permitted revocation delay. Deliver matching realm configuration requirements.
- [ ] 1.5 Add official Keycloak hosting with stable port, persistent volume, readiness, secrets, and OTLP telemetry. Validate those resources through Aspire.
- [ ] 1.6 Add local realm, client, role, and audience initialization with explicit reconciliation. Validate fresh startup and restart without account loss.
- [ ] 1.7 Register `AddKeycloakJwtBearer`, raw claims, and production HTTPS authority validation. Validate real-token acceptance and invalid-token rejection.
- [ ] 1.8 Add authentication and authorization middleware before FastEndpoints. Validate protected progress requests require authentication when their endpoints exist.
- [ ] 1.9 Add identity and treatment domain types, Vogen IDs, and invariants. Validate domain unit tests and converter registration.
- [ ] 1.10 Add identity persistence, unique mapping constraints, and an EF migration. Validate PostgreSQL uniqueness and relationship integration tests.
- [ ] 1.11 Implement trusted provisioning recovery with disabled incomplete mappings. Validate retries create no duplicate mappings or domain records.
- [ ] 1.12 Add API role normalization, current identity resolution, and stable audit mapping. Validate unrelated roles, dual roles, and audit identifiers.
- [ ] 1.13 Add shared ownership, treatment, patient, and plan grant authorization policies. Validate disabled mappings, revoked treatment, and cross-patient denials.
- [ ] 1.14 Add real Keycloak integration fixtures and token clients. Validate issuer, audience, signature, expiration, rotation, and ID-token rejection.
- [ ] 1.15 Add Swagger authorization code flow with PKCE and explicit redirect configuration. Validate interactive login produces a usable API access token.
- [ ] 1.16 Add idempotent development identity and treatment seeds through `MigrationService`. Validate stable subjects and development-only execution after migrations.
- [ ] 1.17 Document credentials, provisioning recovery, realm reconciliation, issuer configuration, and restart behavior under `tools/AppHost/Keycloak/`. Validate the local procedure.
- [ ] 1.18 Guard local realm import from production publication. Validate the production configuration requires explicit HTTPS authority without development file injection.

## 2. Milestone 1 — Garage foundations

- [ ] 2.1 Pin a documented Garage image and compatible configuration. Deliver the selected version and validated configuration under `tools/AppHost/Garage/`.
- [ ] 2.2 Add the custom Garage resource, metadata and data volumes, endpoints, and secret parameters. Validate resource readiness through Aspire.
- [ ] 2.3 Add idempotent node layout, private bucket, and scoped access key initialization. Validate fresh startup and initialization retries against existing state.
- [ ] 2.4 Add API configuration and startup dependencies for completed Garage initialization. Validate the API waits for actual storage readiness.
- [ ] 2.5 Configure browser-reachable S3 endpoints and CORS. Validate a signed browser request reaches the intended object without administrative access.
- [ ] 2.6 Add `IObjectStorage` and the `AWSSDK.S3` Garage adapter with path-style addressing. Validate private put, read, signing, and removal operations.
- [ ] 2.7 Validate supported Garage primitives for exact-content promotion during staging replacement. Deliver a documented mechanism and a real-storage race check.
- [ ] 2.8 Document local storage initialization, credentials, endpoints, and restart behavior. Validate metadata and object bytes survive an Aspire restart.

## 3. Milestone 1 — Schedule and typed ID foundations

- [ ] 3.1 Add the progress typed ID foundations and register every converter. Validate the EF model builds without missing conversions.
- [ ] 3.2 Record deterministic ambiguous and invalid local boundary rules. Deliver explicit timezone examples before schedule implementation.
- [ ] 3.3 Implement daily and start-anchored weekly calendar periods with inclusive end dates. Validate domain tests for shortened final periods.
- [ ] 3.4 Implement IANA timezone boundary conversion and API `TimeProvider` registration. Validate daylight saving transitions without fixed-hour assumptions.
- [ ] 3.5 Define shared due, missed, and late calculations. Validate incomplete drafts, future periods, late submissions, and ended-plan exclusions.
- [ ] 3.6 Document the schedule defaults and milestone 1 completion criteria. Validate identity, storage, restart, and schedule checks together.

## 4. Milestone 2 — Forms and publishing

- [ ] 4.1 Record form, question, text, instruction, choice, and option limits. Deliver constants and examples before form validation implementation.
- [ ] 4.2 Add form, version, and question domain types for all five question types. Validate domain rules and one-file attachment limits.
- [ ] 4.3 Add stable question keys and version-specific question and option identifiers. Validate historical answer meaning survives later definition changes.
- [ ] 4.4 Add form persistence, unique version numbers, concurrency tokens, and migrations. Validate PostgreSQL relationships and immutable version preservation.
- [ ] 4.5 Add `CreateProgressForm` and `UpdateProgressFormDraft` slices with Swagger summaries. Validate ownership, supplied limits, and stale-update `409` responses.
- [ ] 4.6 Add `DuplicateProgressForm` and `PublishProgressForm` slices. Validate independent duplication, whole-form validation, and unique concurrent publication numbers.
- [ ] 4.7 Add `GetProgressForm` and `GetProgressForms` slices with scoped queries. Validate list filtering and unauthorized detail responses.
- [ ] 4.8 Document form lifecycle and version contracts through endpoint summaries. Validate all five question types and later publication through the API.

## 5. Milestone 3 — Plans and assignments

- [ ] 5.1 Add plan domain state with immutable assignment references and schedule values. Validate replacement preserves the previous plan's history.
- [ ] 5.2 Add plan persistence, concurrency tokens, plan grant persistence, and migrations. Validate PostgreSQL relationships and update conflicts.
- [ ] 5.3 Add `AssignProgressPlan` with published-version and treatment authorization. Validate daily and weekly assignments without appointment dependence.
- [ ] 5.4 Add `GetProgressPlan` and `GetPatientProgressPlans` with scoped queries. Validate patient ownership and doctor grant-plus-treatment requirements.
- [ ] 5.5 Add plan closure domain behavior without losing entries. Validate closed plans reject new draft changes and submissions.
- [ ] 5.6 Document assignment replacement and plan access through endpoint summaries. Validate earlier assignments remain tied to their published version.

## 6. Milestone 3 — Draft entries and answer validation

- [ ] 6.1 Record answer text limits and submitted entry retention rules. Deliver validation and retention decisions without adding submission corrections.
- [ ] 6.2 Add entry and answer domain types with one value representation per answer. Validate typed values and optional-versus-required distinctions.
- [ ] 6.3 Add entry persistence, period uniqueness, concurrency tokens, and migrations. Validate PostgreSQL constraints and concurrent draft creation.
- [ ] 6.4 Add `GetDueProgressEntries` and `GetProgressEntry` with schedule and access checks. Validate future periods and unauthorized reads return no data.
- [ ] 6.5 Add `SaveProgressEntryDraft` with published-version validation. Validate partial drafts, unknown questions, duplicates, invalid options, and invalid values.
- [ ] 6.6 Add draft concurrency handling and immutable submitted-state guards. Validate stale updates return `409` and submitted answers cannot change.
- [ ] 6.7 Document draft eligibility and answer validation through endpoint summaries. Validate client-supplied identities and timestamps cannot bypass server rules.

## 7. Milestone 3 — Uploads, finalization, and downloads

- [ ] 7.1 Record formats, sizes, expiration, cleanup timing, media retention, and quotas. Deliver the decisions before upload endpoint implementation.
- [ ] 7.2 Add media domain state, persistence, relationships, and migrations. Validate lifecycle invariants and PostgreSQL associations.
- [ ] 7.3 Add `RequestMediaUpload` for authorized draft attachment questions. Validate server-selected staging keys and temporary browser-accessible URLs.
- [ ] 7.4 Add finalization validation for ownership, size, expiration, and actual format. Validate missing, expired, invalid, and unauthorized uploads remain unavailable.
- [ ] 7.5 Promote exactly validated bytes to a separate final key before readiness. Validate staging replacement and attempted finalized-content overwrite against Garage.
- [ ] 7.6 Make finalization recoverable and idempotent. Validate repeated requests and injected storage failures preserve one logical asset and safe retries.
- [ ] 7.7 Add `GetMediaDownloadUrl` with entry authorization. Validate private downloads, URL expiration, patient isolation, and authorized doctor access.
- [ ] 7.8 Document media lifecycle, signed URL limitations, and failure recovery through endpoint summaries. Validate upload-to-ready behavior against real Garage.

## 8. Milestone 3 — Submission, closure, and cleanup coordination

- [ ] 8.1 Establish the shared PostgreSQL coordination protocol for plans, entries, and assets. Validate consistent lock order through focused concurrent tests.
- [ ] 8.2 Add `SubmitProgressEntry` against the saved draft and assigned version. Validate required answers, server time, and permitted late periods.
- [ ] 8.3 Validate ready asset ownership, entry, and version-specific question before submission. Validate pending, expired, missing, and mismatched asset rejection.
- [ ] 8.4 Commit entry submission and asset attachment in one transaction. Validate rollback preserves both states and successful submission attaches all assets.
- [ ] 8.5 Implement submission retries and concurrent submissions. Validate one submitted entry per period and no additional write on a successful retry.
- [ ] 8.6 Add `EndProgressPlan` using the shared coordination protocol. Validate closure races and draft updates cannot commit through a closed plan.
- [ ] 8.7 Add scheduled expiration and abandoned object cleanup with database claims. Validate cleanup cannot delete media attached by concurrent submission.
- [ ] 8.8 Add retryable object deletion after cleanup state coordination. Validate failures preserve submitted media and permit abandoned-object recovery.
- [ ] 8.9 Document submission, `409`, closure, and cleanup behavior through summaries and operational guidance. Validate the shared transaction acceptance scenarios.

## 9. Milestone 4 — History and summaries

- [ ] 9.1 Add `GetProgressHistory` with date filters, pagination, period data, and version-aware answers. Validate filtered responses and access policies.
- [ ] 9.2 Return attachment identifiers and metadata through history. Validate media access still requires a separately authorized temporary download URL.
- [ ] 9.3 Add `GetProgressSummary` with shared schedule calculations. Validate completed, missed, late, incomplete draft, future, and closed-plan counts.
- [ ] 9.4 Add numeric trends with question and version identification. Validate changed scales and meanings remain distinguishable.
- [ ] 9.5 Add development seeds for daily psychology, weekly structured therapy, and weekly exercise videos. Validate idempotent development-only initialization.
- [ ] 9.6 Complete history and summary Swagger documentation. Validate the three clinical workflows and image retrieval through the documented API.

## 10. Milestone 4 — Release integration checks

- [ ] 10.1 Build the solution and run unit, integration, and architecture test projects. Validate all checks pass with real PostgreSQL, Keycloak, and Garage.
- [ ] 10.2 Run the EF pending-model-changes check. Validate generated migrations match the completed model and migration startup succeeds.
- [ ] 10.3 Validate the complete release through Aspire, including Swagger login, signed browser URLs, CORS, readiness, telemetry, and restart persistence.
- [ ] 10.4 Validate cross-patient denial, doctor grants, late entries, version preservation, retries, and transaction races through the release acceptance scenarios.
- [ ] 10.5 Reconcile the implemented behavior with all six deltas and recorded decisions. Validate strict OpenSpec checks and complete only verified tasks.

## Workflow follow-up

- Review the implemented change and its validation evidence before archive.
- Archive the completed change and merge its deltas into `openspec/specs/`.
- Validate the resulting baseline specs and retain the source provenance map.
