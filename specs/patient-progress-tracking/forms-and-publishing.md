# Patient progress tracking: forms and publishing

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestone:** 2, with typed ID foundations in milestone 1.

## Scope and dependencies

Doctors create reusable forms, edit drafts, duplicate forms, and publish immutable versions.
The first release supports text, numeric scale, single-choice, image, and video questions.

This capability depends on [identity and access](identity-and-access.md).
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.
The [media spec](media-and-storage.md) owns file handling for attachment questions.

## Domain model

| Type | Responsibility | Important data |
|---|---|---|
| `ProgressForm` | Owns a reusable form and its draft | Owner `DoctorId`, title, description, draft revision |
| `ProgressFormVersion` | Preserves a published definition | Form ID, version number, publication timestamp, questions |
| `ProgressQuestion` | Defines one question within a version or draft | Stable question key, type, label, instructions, order, required flag, validation rules |

`ProgressForm` owns its versions and questions.
Each question has a stable key across versions. Its database ID identifies the question in one specific version.

Each single-choice option has an identifier and a label.
Answers reference the option identifier from the assigned version.

Numeric questions define a minimum, maximum, optional step, and optional endpoint labels.
The initial scale can use integer values.

Each image or video question accepts one file in the first release.
A doctor can add separate attachment questions for several exercises or images.
Attachment questions use the same schedules and form versions as structured questions.

## Form lifecycle

A doctor can edit a draft and publish it as a new version.
Publication validates the whole form and assigns a unique version number.

Published versions remain immutable.
Their labels, options, question types, and validation rules preserve the meaning of submitted answers.
Published versions also preserve attachment rules and permitted formats.

A plan references one published version.
Publication of a later version does not change existing plans or entries.

The doctor can duplicate a form for a specific patient.
The doctor edits the duplicate, publishes it, and assigns it to that patient.
The [plans spec](plans-and-scheduling.md) owns assignment and replacement rules.

The [entries spec](entries-and-submission.md) owns answer validation against the assigned version.

## Repository scope

Domain types belong in `src/WebApi/Common/Domain/ProgressForms/`.
EF configurations belong in `src/WebApi/Common/Persistence/ProgressForms/`.

The feature group is `src/WebApi/Features/ProgressForms/ProgressFormsGroup.cs`.
The use case folders are:

- `CreateProgressForm/`
- `UpdateProgressFormDraft/`
- `DuplicateProgressForm/`
- `PublishProgressForm/`
- `GetProgressForm/`
- `GetProgressForms/`.

## Acceptance criteria

The implementation meets these criteria:

- A doctor can create, edit, duplicate, and publish their own forms.
- A published form supports all five question types and their validation rules.
- Each published version has a unique version number.
- Later draft edits and publications preserve existing versions, plans, and historical answers.
- Concurrent conflicting form updates return `409`.

## Validation

Unit tests cover question rules, form publication, and publication history.
PostgreSQL integration tests cover relationships, migrations, unique version numbers, and version preservation.
Endpoint tests cover form ownership, draft validation, duplication, publication, and conflicting updates.

## Open decisions

The form implementation resolves these limits:

- Form size and question counts
- Question text and instruction lengths
- Choice counts and option label lengths.

The [entries spec](entries-and-submission.md) tracks answer text limits.
The [media spec](media-and-storage.md) tracks media limits and permitted formats before upload implementation.
