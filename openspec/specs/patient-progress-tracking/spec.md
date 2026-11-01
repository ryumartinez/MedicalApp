# Patient progress tracking Specification

## Purpose

Define the first release of patient progress tracking through doctor-owned forms, scheduled patient submissions, private media, and progress reports.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Patient progress tracking implementation plan](../../../specs/patient-progress-tracking.md).
Schedule and submission defaults remain proposed. Open decisions in the capability specs remain unresolved.

## Capability map

| Capability | Responsibility | Dependencies |
|---|---|---|
| [Identity and access](identity-and-access/spec.md) | Keycloak, identity mappings, treatment relationships, and access policies | Foundation for protected endpoints |
| [Forms and publishing](forms-and-publishing/spec.md) | Draft forms, questions, duplication, and immutable published versions | Identity and access |
| [Plans and scheduling](plans-and-scheduling/spec.md) | Assignments, periods, timezones, and plan closure | Identity and published forms |
| [Entries and submission](entries-and-submission/spec.md) | Draft answers, validation, submission, and the shared transaction contract | Plans, published forms, and media for attachments |
| [Media and storage](media-and-storage/spec.md) | Garage, private uploads and downloads, asset states, and cleanup | Storage foundations and authorized draft entries |
| [History and summaries](history-and-summaries/spec.md) | Filtered history, completion counts, and numeric trends | Access policies, versions, schedules, entries, and media metadata |

## Requirements

### Requirement: First release progress workflow
The API MUST support doctor-owned forms, daily and weekly assignments, patient drafts, final submissions, and doctor review.
The first release MUST support text, numeric scale, single-choice, image, and video questions.

#### Scenario: Daily psychology plan
- **WHEN** a psychologist assigns a daily form for mood and energy and the patient submits answers
- **THEN** the authorized psychologist can review those answers and the energy history.

#### Scenario: Weekly physical therapy plan
- **WHEN** a physical therapist assigns a weekly form for pain, repetitions, and exercise notes
- **THEN** the patient can submit those answers for each eligible weekly period.

#### Scenario: Physical therapy with video
- **WHEN** a physical therapist assigns a weekly video question with optional notes
- **THEN** the patient can submit an exercise video and the authorized therapist can review it.

### Requirement: Progress independent of appointments
Progress tracking MUST operate independently of appointments.
A patient MUST be able to hold several plans from different doctors under the applicable access policies.

#### Scenario: Plans without an appointment
- **WHEN** authorized doctors assign different plans to a patient without an appointment
- **THEN** each plan retains its own doctor, schedule, entries, and access restrictions.

### Requirement: Historical meaning preserved across capabilities
Forms, plans, entries, and reports MUST preserve the meaning of answers through their assigned published form version.

#### Scenario: Publication after an assignment
- **WHEN** a doctor publishes a later form version
- **THEN** an existing plan and its historical answers retain their original version and question definitions.

### Requirement: Shared submission contract
Entries, plan closure, and media cleanup MUST obey the transaction contract in [Entries and submission](entries-and-submission/spec.md).

#### Scenario: Concurrent submission attempts
- **WHEN** two requests submit the same reporting period concurrently
- **THEN** the database contains one submitted entry for that plan and period.

#### Scenario: Submission with an ineligible attachment
- **WHEN** a required attachment references a pending, expired, missing, or unauthorized asset
- **THEN** submission fails without attaching that asset.

### Requirement: Private progress and media
All progress and media endpoints MUST enforce the identity and resource policies in [Identity and access](identity-and-access/spec.md).

#### Scenario: Another patient's data
- **WHEN** a patient requests another patient's progress or media
- **THEN** the API denies access under the specified authentication, authorization, and resource response rules.

#### Scenario: Another doctor's plan
- **WHEN** a doctor lacks an explicit grant or active treatment relationship for another doctor's plan
- **THEN** the API excludes that plan from the doctor's permitted scope.

### Requirement: Repository implementation conventions
Each use case MUST follow the repository's vertical slice conventions for endpoints, requests, validators, responses, and Swagger summaries.
Domain types MUST use typed IDs, guarded factories, private EF constructors, and the applicable aggregate conventions.
Persistence MUST register Vogen converters, DbSets, configurations, indexes, and migrations through the existing migration service.

#### Scenario: New progress capability
- **WHEN** implementation adds a progress capability
- **THEN** its domain, persistence, endpoints, and tests follow the source plan's shared repository conventions.
- **AND** queries use aggregate specification classes with static factory methods and namespaces match their folders.

### Requirement: Concurrency and current time
Forms, plans, and entries MUST use optimistic concurrency where updates can conflict and return `409` for conflicting updates.
The API MUST use `TimeProvider` for current time.

#### Scenario: Conflicting update
- **WHEN** a caller changes a form, plan, or entry with a conflicting revision
- **THEN** the API returns `409` under that capability's retry rules.

### Requirement: Foundations and release validation
Milestone 1 MUST establish Keycloak, Garage, identity mappings, access policies, typed IDs, and schedule foundations.
Release validation MUST include the solution build, all test projects, architecture checks, and an EF pending-model-changes check.
Integration tests MUST use PostgreSQL through Testcontainers with Respawn resets.

#### Scenario: Restart of local foundations
- **WHEN** the Aspire application restarts after initialization and a media upload
- **THEN** repeatable initialization preserves identity state, Garage metadata, and uploaded files.

#### Scenario: Integration test database
- **WHEN** an integration test uses the shared database fixture
- **THEN** Testcontainers supplies PostgreSQL and Respawn resets test data between tests while preserving the migration history.

### Requirement: API delivery and release boundary
The first release MUST provide API responses for a future form builder, patient form screen, and doctor progress view.
Frontend implementation MUST require a separate plan.

#### Scenario: Release acceptance
- **WHEN** the first release completes its four milestones
- **THEN** psychology and structured or video-based physical therapy workflows operate through the API.
- **AND** frontend work, reminders, doctor feedback, corrections, conditional questions, and advanced reports remain outside this release.

## Domain map

| Type | Responsibility | Detailed spec |
|---|---|---|
| `ProgressForm` | Owns a reusable form and its draft | [Forms and publishing](forms-and-publishing/spec.md) |
| `ProgressFormVersion` | Preserves a published definition | [Forms and publishing](forms-and-publishing/spec.md) |
| `ProgressQuestion` | Defines one question within a version or draft | [Forms and publishing](forms-and-publishing/spec.md) |
| `ProgressPlan` | Assigns a published version to one patient | [Plans and scheduling](plans-and-scheduling/spec.md) |
| `ProgressEntry` | Records answers for one reporting period | [Entries and submission](entries-and-submission/spec.md) |
| `ProgressAnswer` | Stores a typed answer to one question | [Entries and submission](entries-and-submission/spec.md) |
| `MediaAsset` | Tracks an uploaded file and its lifecycle | [Media and storage](media-and-storage/spec.md) |

## Shared repository conventions

Each capability includes its domain rules, persistence, endpoints, and tests.
Each child spec lists its proposed folders and use cases.

The implementation follows these conventions:

- Each use case contains its endpoint, request, response where applicable, validator, and Swagger summary.
- Each domain folder contains its related entities, value objects, errors, and specifications. Namespaces mirror their folders.
- Entities use static factories, private EF constructors, and property guards. Types that raise domain events use `AggregateRoot<TId>`.
- Related domain objects reference each other through typed IDs.
- Every new Vogen ID needs registration in `VogenEfCoreConverters.cs`.
- Queries use aggregate specification classes with static factory methods.
- EF configurations define relationships, field limits, concurrency tokens, and indexes.
- DbSet properties belong in partial `ApplicationDbContext` files.
- Database migrations run through the existing migration service.
- Forms, plans, and entries use optimistic concurrency where concurrent changes can conflict. The API returns `409` for conflicting updates.
- The API uses `TimeProvider` for current time.
- Development seed data includes sample daily and weekly plans.

## Milestones

| Milestone | Work | Completion criteria |
|---|---|---|
| 1. Access, storage, and foundations | Keycloak through Aspire, identity mappings, access policies, typed IDs, schedules, and Garage | Repeatable initialization, persistent state, and passing access and schedule tests |
| 2. Forms | Drafts, updates, duplication, publication, persistence, and all five question types | Published versions remain immutable after later edits |
| 3. Plans, entries, and media | Assignments, periods, drafts, uploads, validation, submission, downloads, cleanup, and closure | Authorized submissions and files with concurrency protection |
| 4. History and summaries | History, attachment access, counts, trends, Swagger documentation, and seed data | Psychology and video-based physical therapy work through the API |

All four milestones form the first release.
Garage and Aspire foundations precede upload workflows and progress endpoints.
Development seed data includes daily and weekly plans.

## Open decisions

Each capability retains its own open decisions and proposed defaults.
Media limits and quotas require decisions before upload implementation.
Reminder channels and delivery timing belong to a later release.
The source plan retains the detailed domain map and implementation context.
