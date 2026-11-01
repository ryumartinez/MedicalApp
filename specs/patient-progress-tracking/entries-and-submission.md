# Patient progress tracking: entries and submission

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestone:** 3, with typed ID foundations in milestone 1.

## Scope and dependencies

Patients save draft answers and submit entries for scheduled reporting periods.
This capability depends on [identity and access](identity-and-access.md), [published forms](forms-and-publishing.md), and [plans and scheduling](plans-and-scheduling.md).
Attachment answers also depend on [media and storage](media-and-storage.md).
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.

This spec owns the submission transaction contract shared by entries, plans, and media.

## Domain model

| Type | Responsibility | Important data |
|---|---|---|
| `ProgressEntry` | Records answers for one reporting period | Plan ID, period start date, UTC period boundaries, status, submission timestamp |
| `ProgressAnswer` | Stores a typed answer to one question | Entry ID, version-specific question ID, text value, numeric value, option ID, or `MediaAssetId` |

`ProgressEntry` owns its answers.
Entries reference related domain objects through typed IDs.

Answers use fields appropriate to the question type.
Only one value representation can contain an answer.
An omitted optional answer differs from an empty required answer.

## Entry lifecycle and validation

Entries have two states: `Draft` and `Submitted`.

Drafts can contain incomplete answers.
A draft save validates supplied values but permits missing required answers.

Each answer must reference a question in the assigned form version.
The API rejects unknown questions, duplicate answers, incorrect types, invalid options, and values outside the configured limits.
Single-choice answers reference an option identifier from the assigned version.

Submission validates all required answers against the assigned form version.
A submitted entry remains immutable.

Submission operates on the saved draft.
A submission retry returns the existing submitted entry without another write or duplicate entry.

The [schedule spec](plans-and-scheduling.md) defines eligible periods, late submissions, and plan closure rules.
The API uses `TimeProvider` for current time.
Clients cannot choose the submission timestamp or bypass the schedule with a supplied patient ID.

## Submission transaction contract

The final submission transaction must validate that the plan still permits submission.
A concurrent plan closure must not permit a submission after closure.
The [plan implementation](plans-and-scheduling.md#plan-closure-and-submission) must coordinate with this transaction.

Attachment answers must reference ready assets owned by the patient.
Each asset must belong to the same entry and version-specific question.

Submission marks referenced assets attached in the same database transaction.
Only submission can move a ready asset to the attached state.
Cleanup must not remove attached assets or race with submission.
The [media implementation](media-and-storage.md#asset-lifecycle-and-cleanup) must coordinate its database state changes with this transaction.

PostgreSQL enforces a unique constraint on `(ProgressPlanId, PeriodStartDate)`.
Concurrent draft creation must resolve the conflict without two entries.
Concurrent submissions must produce one submitted entry for the period.

Entries use optimistic concurrency where concurrent changes can conflict.
The API returns `409` for conflicting updates.
Submission retries retain the idempotent behavior defined in the entry lifecycle.

## Repository scope

Domain types belong in `src/WebApi/Common/Domain/ProgressEntries/`.
EF configurations belong in `src/WebApi/Common/Persistence/ProgressEntries/`.

The feature group is `src/WebApi/Features/ProgressEntries/ProgressEntriesGroup.cs`.
The use case folders are:

- `GetDueProgressEntries/`
- `GetProgressEntry/`
- `SaveProgressEntryDraft/`
- `SubmitProgressEntry/`.

The [history spec](history-and-summaries.md) adds reporting use cases to the same feature group.

## Acceptance criteria

The implementation meets these criteria:

- A patient can save incomplete drafts and submit valid answers for their own permitted periods.
- The API rejects invalid question references, answer types, options, and values.
- Submission requires all required answers and eligible attachment assets.
- Submitted entries remain immutable, and submission retries return the existing entry.
- Concurrent draft creation and submission preserve one entry per reporting period.
- The submission transaction coordinates plan closure, asset attachment, and cleanup.
- Late submissions retain the original reporting period and actual submission timestamp.

## Validation

Unit tests cover answer types, required answers, entry transitions, and late submissions.
PostgreSQL integration tests cover relationships, migrations, unique period constraints, concurrent draft creation, concurrent submission, and plan closure races.
Endpoint tests cover authorization, invalid version references, unknown questions, duplicate answers, incorrect types, invalid values, and idempotent submission retries.
Attachment tests reject pending, expired, missing, or unauthorized assets, including assets for another entry or question.
Concurrency tests cover cleanup against submission and conflicting draft updates.

## Open decisions

The entry implementation resolves answer text limits and retention rules for submitted entries.
Correction rules remain a decision for a later release. The first release keeps submitted entries immutable.
