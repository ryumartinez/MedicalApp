# Patient progress tracking implementation plan

**Status:** Proposed.

Doctors create custom forms and assign them to patients. Patients submit answers on a daily or weekly schedule. Doctors review the answers over time.

The first release supports structured answers, images, and videos. Garage provides private S3-compatible storage. Aspire provides local orchestration from milestone 1.

## Specification map

This parent spec owns the release scope, shared repository conventions, milestones, and end-to-end acceptance scenarios.
Each child spec owns its detailed rules, implementation scope, validation, and open decisions.

| Spec | Scope | Dependencies |
|---|---|---|
| [Identity and access](patient-progress-tracking/identity-and-access.md) | Keycloak authentication, identity mappings, treatment relationships, access policies | Foundation for all protected endpoints |
| [Forms and publishing](patient-progress-tracking/forms-and-publishing.md) | Drafts, questions, validation, duplication, immutable versions | Identity and access |
| [Plans and scheduling](patient-progress-tracking/plans-and-scheduling.md) | Assignments, reporting periods, timezones, plan closure | Identity and published forms. Schedule foundations start in milestone 1. |
| [Entries and submission](patient-progress-tracking/entries-and-submission.md) | Draft answers, validation, submission, idempotency, concurrency | Plans and published forms. Attachments also require media. |
| [Media and storage](patient-progress-tracking/media-and-storage.md) | Garage, Aspire, uploads, downloads, asset lifecycle, cleanup | Garage foundations start in milestone 1. Upload workflows require authorized draft entries. |
| [History and summaries](patient-progress-tracking/history-and-summaries.md) | Filtered history, completion counts, numeric trends | Access policies, form versions, schedules, entries, media metadata |

The entries spec owns the shared submission transaction contract, including plan closure and media attachment.
The plans and media specs reference that contract.

## User workflows

The doctor creates a draft form, adds questions, and publishes a version. The doctor then assigns that version to a patient with a schedule.

The patient sees the forms due, saves draft answers, and submits an entry. The doctor reviews the patient's history and numeric trends.

Progress tracking is independent of appointments. A patient can have several plans from different doctors.

| Use case | Questions | Schedule | Release |
|---|---|---|---|
| Psychology | "How do you feel today?" and "What is your energy level?" | Daily | First release |
| Physical therapy | Pain level, exercise repetitions, and exercise notes | Weekly | First release |
| Physical therapy with video | A video of the patient doing the exercises, with optional notes | Weekly | First release |

## First release scope

The first release includes:

- Forms owned by a doctor, with editable drafts and immutable published versions
- Text, numeric scale, single-choice, image, and video questions
- Required questions, instructions, display order, and validation rules
- Patient assignments with daily or weekly schedules
- Draft answers and final submissions
- Due periods, missed periods, and completion counts
- Paginated answer history and numeric trends
- Garage storage through Aspire, with persistent data and automatic initialization
- Authorized uploads and downloads through temporary signed URLs
- File validation, upload expiration, and abandoned upload cleanup
- Keycloak authentication and access rules for doctors and patients.

Later releases can add reminders, doctor feedback, corrections to submitted entries, conditional questions, and advanced reports.
Reminder channels and delivery timing remain decisions for a later release.

The first release covers both psychology and video-based physical therapy.
It provides API responses for a future form builder, patient form screen, and doctor progress view.
Frontend implementation needs a separate plan.

## Domain map

| Type | Responsibility | Detailed spec |
|---|---|---|
| `ProgressForm` | Owns a reusable form and its draft | [Forms and publishing](patient-progress-tracking/forms-and-publishing.md) |
| `ProgressFormVersion` | Preserves a published definition | [Forms and publishing](patient-progress-tracking/forms-and-publishing.md) |
| `ProgressQuestion` | Defines one question within a version or draft | [Forms and publishing](patient-progress-tracking/forms-and-publishing.md) |
| `ProgressPlan` | Assigns a published version to one patient | [Plans and scheduling](patient-progress-tracking/plans-and-scheduling.md) |
| `ProgressEntry` | Records answers for one reporting period | [Entries and submission](patient-progress-tracking/entries-and-submission.md) |
| `ProgressAnswer` | Stores a typed answer to one question | [Entries and submission](patient-progress-tracking/entries-and-submission.md) |
| `MediaAsset` | Tracks an uploaded file and its lifecycle | [Media and storage](patient-progress-tracking/media-and-storage.md) |

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

## Implementation milestones

| Milestone | Work | Completion criteria |
|---|---|---|
| 1. Access, storage, and foundations | Keycloak through Aspire, identity mappings, access policies, typed IDs, schedules, Garage through Aspire, persistent volumes, secrets, and initialization | Aspire starts Keycloak and Garage. Initialization is repeatable. Identity and storage state survive restarts. Access and schedule tests pass. |
| 2. Forms | Draft creation, updates, duplication, publication, persistence, and all question types | A doctor can publish text, scale, choice, image, and video questions. Later edits preserve published versions. |
| 3. Plans, entries, and media | Assignments, reporting periods, drafts, uploads, validation, submission, downloads, cleanup, and plan closure | A patient can submit structured answers and files with concurrency protection and authorized access. |
| 4. History and summaries | Filtered history, attachment access, completion counts, numeric trends, Swagger documentation, and seed data | Both psychology and video-based physical therapy work through the API. |

All four milestones form the first release.
Garage and Aspire integration belong to milestone 1, before the upload workflow and progress endpoints.
The child specs divide responsibilities across these milestones.

## Release validation

Each child spec defines its unit, integration, endpoint, and concurrency tests.
Architecture tests enforce the repository conventions.
Release validation includes the solution build, all test projects, and an EF pending-model-changes check.

The end-to-end acceptance scenarios are:

- A psychologist assigns a daily mood form and sees the patient's submitted answers and energy history.
- A physical therapist assigns a weekly form for pain, repetitions, and exercise notes.
- A physical therapist assigns a weekly video question and reviews the patient's submitted exercise video.
- A patient submits an image answer and an authorized doctor can retrieve it through a temporary URL.
- Required attachment questions reject pending, expired, missing, or unauthorized assets.
- Retrying upload finalization or entry submission preserves the same file and entry.
- Restarting the Aspire application preserves Garage metadata and uploaded files.
- A doctor publishes a new form version without altering an existing plan or its historical answers.
- Two concurrent submissions produce one submitted entry for the period.
- A late submission appears in the correct reporting period.
- A patient cannot read or change another patient's progress.
- A doctor cannot read another doctor's plans without an explicit access grant.

## Decision tracking

The schedule and submission rules remain proposed defaults. Changes to those defaults must remain explicit in the implementation.

Each child spec records the decisions for its capability.
Keycloak is the selected authentication provider. Its implementation must use the official Aspire hosting and authentication integrations.
Identity mappings and treatment relationship management belong to milestone 1.
Media limits and quotas need decisions before the upload implementation.
