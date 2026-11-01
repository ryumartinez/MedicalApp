# Entries and submission Specification

## Purpose

Define typed patient answers, incomplete drafts, immutable submissions, and database coordination across plans, entries, and media assets.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Entries and submission](../../../../specs/patient-progress-tracking/entries-and-submission.md).
Parent: [Patient progress tracking](../spec.md).
Milestone: 3, with typed ID foundations in milestone 1.
Dependencies: [Identity](../identity-and-access/spec.md), [Forms](../forms-and-publishing/spec.md), [Plans](../plans-and-scheduling/spec.md), and [Media](../media-and-storage/spec.md) for attachments.
Schedule and submission defaults remain proposed.

## Requirements

### Requirement: Entry ownership and period metadata
`ProgressEntry` MUST own its answers and reference its plan through a typed ID.
It MUST retain the period start date, UTC boundaries, status, and actual submission timestamp.
Each answer MUST identify its version-specific question and use typed references for related domain objects.

#### Scenario: Late submission metadata
- **WHEN** a patient submits an eligible entry after its reporting period ends
- **THEN** the entry preserves the original period and records the server's actual submission timestamp.

### Requirement: One typed answer representation
Answers MUST use exactly one applicable value representation: text, numeric value, option identifier, or `MediaAssetId`.
An omitted optional answer MUST remain distinct from an empty required answer.

#### Scenario: Several value representations
- **WHEN** a supplied answer uses multiple value representations or a representation that conflicts with its question type
- **THEN** the API rejects that answer.

#### Scenario: Omitted optional answer
- **WHEN** a draft omits an optional question
- **THEN** submission does not treat that omission as an empty required answer.

### Requirement: Incomplete but validated drafts
Entries MUST use the states `Draft` and `Submitted`.
A draft save MUST validate supplied values while permitting missing required answers.
Patients MUST save drafts only for their own eligible reporting periods.

#### Scenario: Save an incomplete draft
- **WHEN** a patient saves valid supplied answers without all required answers
- **THEN** the API saves a draft for the eligible period.

### Requirement: Assigned-version answer validation
Every answer MUST reference a question in the assigned published version.
The API MUST reject unknown questions, duplicate answers, incorrect types, invalid options, and values outside configured limits.
Single-choice answers MUST reference option identifiers from that version.

#### Scenario: Question from another version
- **WHEN** an answer references a question outside the plan's assigned version
- **THEN** the API rejects the answer.

#### Scenario: Invalid answer set
- **WHEN** supplied answers contain duplicates, incorrect types, invalid options, or out-of-range values
- **THEN** draft validation and submission validation reject those values.

### Requirement: Submission of the saved draft
Submission MUST operate on the saved draft and validate all required answers against the assigned form version.
The API MUST use `TimeProvider` for current time and MUST NOT accept client-selected submission timestamps or acting patient IDs.
Period eligibility MUST follow [Plans and scheduling](../plans-and-scheduling/spec.md).

#### Scenario: Missing required answer
- **WHEN** a patient submits a saved draft without a required answer
- **THEN** the API rejects submission and the entry remains unsubmitted.

#### Scenario: Client attempts to bypass the schedule
- **WHEN** a request supplies another acting patient ID or a submission timestamp to bypass period eligibility
- **THEN** the API uses the mapped patient and server time for its decision.

### Requirement: Immutable submissions and idempotent retries
Submitted entries MUST remain immutable.
A submission retry MUST return the existing submitted entry without another write or duplicate entry.

#### Scenario: Retry a completed submission
- **WHEN** the caller retries an already successful submission
- **THEN** the API returns the same submitted entry without changing its answers or submission timestamp.

#### Scenario: Edit a submitted entry
- **WHEN** a caller attempts to change answers after submission
- **THEN** the API preserves the submitted entry unchanged.

### Requirement: One entry per plan and period
PostgreSQL MUST enforce uniqueness on `(ProgressPlanId, PeriodStartDate)`.
Concurrent draft creation MUST resolve conflicts without two entries.
Concurrent submissions MUST produce one submitted entry for the period.

#### Scenario: Concurrent draft creation
- **WHEN** two requests create a draft for the same plan and period concurrently
- **THEN** the database retains one entry for that pair.

#### Scenario: Concurrent submission
- **WHEN** two requests submit the same saved draft concurrently
- **THEN** one submitted entry remains for that period and retries retain idempotent behavior.

### Requirement: Conflicting entry updates
Entries MUST use optimistic concurrency where updates can conflict.
The API MUST return `409` for conflicting updates while preserving idempotent submission retries.

#### Scenario: Conflicting draft revisions
- **WHEN** a patient saves a draft revision that conflicts with a committed update
- **THEN** the API returns `409` without overwriting that update.

### Requirement: Atomic plan and attachment validation
The final submission transaction MUST verify that the plan still permits submission.
Referenced assets MUST be ready, belong to the patient, and match the same entry and version-specific question.
Submission MUST mark referenced assets attached in the same database transaction.

#### Scenario: Ineligible attachment
- **WHEN** a required attachment references a pending, expired, missing, foreign-owned, wrong-entry, or wrong-question asset
- **THEN** submission fails without marking that asset attached.

#### Scenario: Successful attachment submission
- **WHEN** a valid submission references eligible ready assets
- **THEN** the entry becomes submitted and those assets become attached in one database transaction.

### Requirement: Coordination with closure and cleanup
Plan closure MUST coordinate with submission so that no submission commits after closure.
Only submission MUST move an asset from ready to attached.
Cleanup MUST coordinate database state with submission and MUST NOT remove attached assets.

#### Scenario: Closure races with submission
- **WHEN** plan closure and final submission run concurrently
- **THEN** database coordination prevents a submission from committing after closure.

#### Scenario: Cleanup races with submission
- **WHEN** cleanup and submission act on the same ready asset concurrently
- **THEN** database coordination prevents cleanup from removing an asset that submission attaches.

## Submission transaction contract

The atomic validation and coordination requirements define the shared submission contract.
[Plan closure](../plans-and-scheduling/spec.md) and [Media cleanup](../media-and-storage/spec.md) must participate in that contract.
The implementation must coordinate plan eligibility, period uniqueness, entry state, asset eligibility, attachment, and cleanup through database transactions.

## Repository scope

Domain types belong in `src/WebApi/Common/Domain/ProgressEntries/`.
EF configurations belong in `src/WebApi/Common/Persistence/ProgressEntries/`.

The feature group is `src/WebApi/Features/ProgressEntries/ProgressEntriesGroup.cs`.
The use case folders are:

- `GetDueProgressEntries/`
- `GetProgressEntry/`
- `SaveProgressEntryDraft/`
- `SubmitProgressEntry/`.

The [history spec](../history-and-summaries/spec.md) adds reporting use cases to the same feature group.

## Validation

Unit tests cover answer types, required answers, entry transitions, and late submissions.
PostgreSQL integration tests cover relationships, migrations, unique period constraints, concurrent draft creation, concurrent submission, and plan closure races.
Endpoint tests cover authorization, invalid version references, unknown questions, duplicate answers, incorrect types, invalid values, and idempotent submission retries.
Attachment tests reject pending, expired, missing, or unauthorized assets, including assets for another entry or question.
Concurrency tests cover cleanup against submission and conflicting draft updates.

## Open decisions

The entry implementation resolves answer text limits and retention rules for submitted entries.
Correction rules remain a decision for a later release. The first release keeps submitted entries immutable.
