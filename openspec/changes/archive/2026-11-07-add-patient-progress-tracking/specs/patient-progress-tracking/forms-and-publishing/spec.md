# Spec Delta

## Purpose

Let doctors maintain reusable forms and publish immutable definitions that preserve the meaning of patient answers over time.

## ADDED Requirements

### Requirement: Manage doctor-owned form drafts

An authorized doctor SHALL create, read, list, and edit their own form drafts.
A draft SHALL contain a title, description, and ordered questions.
Each question SHALL define its label, instructions, type, required flag, and applicable validation rules.

#### Scenario: Create and edit a form draft

- **WHEN** A doctor creates a draft and changes its title, description, or questions.
- **THEN** The saved draft reflects those changes within the configured limits.
- **AND** The draft remains under that doctor's ownership.

#### Scenario: Restrict form management to the owner

- **WHEN** Another doctor requests a form outside their permitted scope.
- **THEN** The API returns no form data and applies the identity capability's detail response rules.

### Requirement: Support all first-release question types

Forms SHALL support text, numeric scale, single-choice, image, and video questions.
Publication MUST validate the entire form and its type-specific rules.
Invalid definitions MUST NOT become published versions.

#### Scenario: Publish all five question types

- **WHEN** A doctor publishes a valid form with text, numeric scale, single-choice, image, and video questions.
- **THEN** The published version retains every question's type, instructions, order, required flag, and validation rules.

#### Scenario: Reject an invalid form definition

- **WHEN** A doctor attempts publication with missing required definition data or invalid question rules.
- **THEN** The API rejects publication and creates no published version.

### Requirement: Define numeric scale rules

A numeric question SHALL define a minimum, maximum, optional step, and optional endpoint labels.
The first implementation can use integer scales.
Published numeric rules MUST remain available for answer validation and interpretation.

#### Scenario: Preserve a numeric scale

- **WHEN** A doctor publishes a numeric question with bounds, a step, and endpoint labels.
- **THEN** The published question preserves those values for patient answers and numeric history.

### Requirement: Identify questions and choice options consistently

Each question SHALL have a stable key across versions and a distinct identifier within its version.
Each single-choice option SHALL have an identifier and a label.
Answers MUST reference the question and option identifiers from the assigned published version.

#### Scenario: Retain question identity across versions

- **WHEN** A doctor publishes a later version containing the same logical question.
- **THEN** Its stable question key remains available across versions.
- **AND** Its version-specific identifier distinguishes it from the previous version's question.

#### Scenario: Preserve the selected option's meaning

- **WHEN** A later draft changes a single-choice label or option set.
- **THEN** An answer for an earlier published version still refers to that version's option identifier and label.

### Requirement: Limit each attachment question to one file

Each image or video question SHALL accept one file in the first release.
Doctors can add separate attachment questions for multiple files.
Published definitions MUST preserve attachment rules and permitted formats.
Attachment questions SHALL use the same form versions and schedules as structured questions.

#### Scenario: Define multiple exercise videos

- **WHEN** A doctor needs separate files for several exercises.
- **THEN** The doctor can publish separate video questions with one file per answer.

#### Scenario: Preserve published attachment rules

- **WHEN** A doctor changes attachment rules in a later draft.
- **THEN** Plans assigned to an earlier version retain that version's attachment rules and permitted formats.

### Requirement: Publish unique immutable versions

Publication SHALL assign a unique version number and record its publication time.
Published versions MUST remain immutable, including question labels, options, types, and validation rules.
A plan MUST reference one published version.
Later draft changes and publications MUST NOT alter existing plans or historical answers.

#### Scenario: Publish a later version

- **WHEN** A doctor edits a draft after publication and publishes it again.
- **THEN** The form gains a distinct version number.
- **AND** Existing versions, assigned plans, and historical answers preserve their original definitions.

#### Scenario: Prevent duplicate publication numbers

- **WHEN** Concurrent publication attempts compete for the same version number.
- **THEN** Persistence prevents duplicate version numbers for that form.
- **AND** Conflicting updates follow the form concurrency rule.

### Requirement: Duplicate a reusable form

A doctor SHALL be able to duplicate their form for patient-specific changes.
The duplicate MUST remain independently editable and publishable.
Changes to the duplicate MUST NOT alter its source form or the source's existing versions and plans.

#### Scenario: Customize a duplicate for one patient

- **WHEN** A doctor duplicates a form, changes the duplicate, and publishes it.
- **THEN** The new version can be assigned through a new plan.
- **AND** The source form and its assigned plans remain unchanged.

### Requirement: Detect conflicting form changes

The API SHALL detect conflicting concurrent changes to a form.
It MUST return `409` for an update that conflicts with a committed change.

#### Scenario: Save competing draft updates

- **WHEN** Two callers update the same draft revision and one update commits first.
- **THEN** The conflicting update returns `409` without overwriting the committed changes.
