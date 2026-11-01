# Forms and publishing Specification

## Purpose

Define reusable doctor-owned forms, validated drafts, and immutable published versions for structured answers and media attachments.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Forms and publishing](../../../../specs/patient-progress-tracking/forms-and-publishing.md).
Parent: [Patient progress tracking](../spec.md).
Milestone: 2, with typed ID foundations in milestone 1.
Dependencies: [Identity and access](../identity-and-access/spec.md) and [Media and storage](../media-and-storage/spec.md) for file handling.

## Requirements

### Requirement: Doctor-owned editable drafts
An authorized doctor MUST be able to create, read, list, and edit their own form drafts.
`ProgressForm` MUST own its draft, versions, and questions and retain its owner, title, description, and draft revision.

#### Scenario: Edit an owned draft
- **WHEN** an authorized doctor updates their form draft
- **THEN** the draft reflects the changes under its revision and ownership rules.

#### Scenario: Conflicting draft updates
- **WHEN** two updates conflict on a form's revision
- **THEN** the API returns `409` for the conflicting update.

### Requirement: Five supported question types
Forms MUST support text, numeric scale, single-choice, image, and video questions.
Each question MUST define a stable key, label, instructions, display order, required flag, and applicable validation rules.

#### Scenario: Publish a mixed form
- **WHEN** a valid draft contains all five question types
- **THEN** publication preserves each question's type, order, instructions, required flag, and validation rules.

### Requirement: Stable question keys and version-specific identifiers
Question keys MUST remain stable across versions.
Each question's database ID MUST identify that question in one specific version.

#### Scenario: A question in successive versions
- **WHEN** a doctor publishes a later version of an existing question
- **THEN** its stable key relates the versions and its version-specific ID identifies the assigned definition.

### Requirement: Numeric scale definitions
Numeric questions MUST define a minimum, maximum, optional step, and optional endpoint labels.
The initial scale can use integer values.

#### Scenario: Publish a numeric scale
- **WHEN** a valid numeric question specifies bounds, a step, and endpoint labels
- **THEN** the published version preserves those rules for entry validation and reporting.

### Requirement: Choice option identifiers
Each single-choice option MUST contain an identifier and label.
Answers MUST reference option identifiers from the assigned version.

#### Scenario: Choice labels change in a later version
- **WHEN** a doctor changes a choice label in a later publication
- **THEN** existing answers retain the option identifier and label from their assigned version.

### Requirement: One file per attachment question
Each image or video question MUST accept one file in the first release.
Attachment questions MUST use the same versions and schedules as structured questions.

#### Scenario: Several exercise attachments
- **WHEN** a doctor requires separate exercise videos or images
- **THEN** the doctor can add separate attachment questions, each with its own single file.

### Requirement: Validated publication and unique versions
Publication MUST validate the whole draft, assign a unique version number, and preserve the publication timestamp and question definitions.

#### Scenario: Valid publication
- **WHEN** a doctor publishes a valid draft
- **THEN** the form gains a published version with a unique number and publication timestamp.

#### Scenario: Invalid publication
- **WHEN** a draft violates its question or form validation rules
- **THEN** publication fails without creating a published version.

### Requirement: Immutable published definitions
Published versions MUST preserve labels, options, question types, validation rules, attachment rules, and permitted formats.
Later edits and publications MUST leave existing versions, plans, and historical answers unchanged.

#### Scenario: Edit a form with an assigned version
- **WHEN** a doctor edits and republishes a form that an existing plan references
- **THEN** the plan and its answers retain their original published definition.

### Requirement: Independent form duplication
A doctor MUST be able to duplicate a form, edit the duplicate, publish it, and assign its published version to a patient.
Assignment MUST follow [Plans and scheduling](../plans-and-scheduling/spec.md).

#### Scenario: Patient-specific form
- **WHEN** a doctor duplicates a form and customizes the duplicate for a patient
- **THEN** the original form remains unchanged and the duplicate can follow its own publication lifecycle.

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

## Validation

Unit tests cover question rules, form publication, and publication history.
PostgreSQL integration tests cover relationships, migrations, unique version numbers, and version preservation.
Endpoint tests cover form ownership, draft validation, duplication, publication, and conflicting updates.

## Open decisions

The form implementation resolves these limits:

- Form size and question counts
- Question text and instruction lengths
- Choice counts and option label lengths.

The [entries spec](../entries-and-submission/spec.md) tracks answer text limits.
The [media spec](../media-and-storage/spec.md) tracks media limits and permitted formats before upload implementation.
