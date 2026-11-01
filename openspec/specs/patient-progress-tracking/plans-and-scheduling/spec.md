# Plans and scheduling Specification

## Purpose

Define patient assignments, local reporting periods, due and missed periods, late submissions, and coordinated plan closure.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Plans and scheduling](../../../../specs/patient-progress-tracking/plans-and-scheduling.md).
Parent: [Patient progress tracking](../spec.md).
Milestones: 1 for schedule foundations and 3 for assignments and closure.
Dependencies: [Identity and access](../identity-and-access/spec.md) and [Forms and publishing](../forms-and-publishing/spec.md).
Schedule and submission defaults remain proposed. Timezone transition handling remains an open decision.

## Requirements

### Requirement: Authorized assignment to a published version
An authorized doctor MUST be able to assign one published form version to one patient with a daily or weekly schedule.
The plan MUST store typed references, its timezone, local start date, optional end date, and ended timestamp.

#### Scenario: Assign a published form
- **WHEN** a doctor with an active treatment relationship assigns a published form version to the patient
- **THEN** the plan records the doctor, patient, published version, and schedule.

#### Scenario: Assignment without a published version
- **WHEN** a doctor attempts to assign an unpublished draft
- **THEN** the API rejects the assignment.

### Requirement: Immutable assignment and replacement
The schedule, timezone, patient, doctor, and form version MUST remain fixed after assignment.
Replacement MUST create a new plan and end the old plan while preserving its history.

#### Scenario: Replace a schedule
- **WHEN** an authorized doctor replaces a patient's assignment with a different schedule
- **THEN** a new plan contains the replacement and the old plan ends with its entries intact.

### Requirement: Local daily and weekly periods
A plan MUST store a valid IANA timezone and a local start date.
A daily period MUST cover one local date.
A weekly period MUST cover seven local dates anchored to the plan's start date.

#### Scenario: Weekly period anchored to assignment
- **WHEN** a weekly plan starts on a given local date
- **THEN** its periods begin on that date and successive seven-date boundaries.

### Requirement: Inclusive end dates
An optional plan end date MUST be inclusive and can shorten the final reporting period.

#### Scenario: End date within a weekly period
- **WHEN** a plan's inclusive end date occurs before the normal weekly boundary
- **THEN** the final period includes the end date and excludes later local dates.

### Requirement: Timezone-aware UTC boundaries
Local period boundaries MUST convert to UTC through the plan's timezone rules, including daylight saving changes.
Scheduling MUST use local dates rather than fixed 24-hour or 168-hour intervals.
The implementation MUST define deterministic transition handling and use `TimeProvider` for current time.

#### Scenario: Daylight saving transition
- **WHEN** a reporting period includes a timezone offset change
- **THEN** its UTC boundaries reflect its local dates and the documented transition rule.
- **AND** its elapsed UTC duration can differ from 24 hours or 168 hours.

### Requirement: Eligible periods and late submission
A patient MUST be able to start an entry only after its period begins.
Each plan MUST permit one entry per reporting period.
A patient MUST be able to submit late while the plan remains active.
Late entries MUST retain their original period and actual submission timestamp.

#### Scenario: Future period
- **WHEN** a patient attempts to start an entry before its reporting period begins
- **THEN** the API rejects that operation.

#### Scenario: Late submission on an active plan
- **WHEN** a patient submits an eligible entry after its reporting period ends while the plan remains active
- **THEN** the entry retains that period and records its actual submission timestamp.

### Requirement: Derived due and missed periods
The API MUST calculate due and missed periods from schedules without requiring empty entries from a background job.
A period that ends without a submitted entry MUST count as missed.
An incomplete draft MUST NOT count as a submission.

#### Scenario: Period with an incomplete draft
- **WHEN** a period ends with only an incomplete draft
- **THEN** the API reports the period as missed.

#### Scenario: Period without an entry
- **WHEN** a scheduled period becomes due without a stored entry
- **THEN** the API can derive it from the plan's schedule.

### Requirement: Consistent completion counts
Completion counts MUST include late submissions and report them separately.
Only scheduled periods MUST contribute to completion counts.
Future periods and periods after plan closure MUST NOT count as missed.

#### Scenario: A missed period gains a submission
- **WHEN** the patient submits a formerly missed period while the plan remains active
- **THEN** reporting changes that period to submitted late and includes it in completion counts.

#### Scenario: Future and ended-plan periods
- **WHEN** reporting calculates missed periods
- **THEN** it excludes future periods and periods after the plan ends.

### Requirement: Coordinated plan closure
Plan closure MUST stop further draft changes and submissions while preserving existing entries.
Closure MUST obey the [submission transaction contract](../entries-and-submission/spec.md#submission-transaction-contract).
Conflicting plan updates MUST return `409`.

#### Scenario: Edit after closure
- **WHEN** a patient attempts a draft change or new submission after plan closure
- **THEN** the API rejects the change and preserves existing entries.

#### Scenario: Closure races with submission
- **WHEN** plan closure and submission run concurrently
- **THEN** their coordinated database transactions prevent a submission from committing after closure.

## Repository scope

Domain types belong in `src/WebApi/Common/Domain/ProgressPlans/`.
EF configurations belong in `src/WebApi/Common/Persistence/ProgressPlans/`.

The feature group is `src/WebApi/Features/ProgressPlans/ProgressPlansGroup.cs`.
The use case folders are:

- `AssignProgressPlan/`
- `EndProgressPlan/`
- `GetProgressPlan/`
- `GetPatientProgressPlans/`.

## Validation

Unit tests cover daily and weekly periods, late submissions, and daylight saving changes.
Schedule tests cover inclusive end dates and periods shortened by the plan's end date.
PostgreSQL integration tests cover relationships, migrations, and concurrent plan closure with submission.
Endpoint tests cover treatment authorization, published version references, schedule restrictions, and plan closure.

## Open decisions

The schedule implementation must define deterministic handling for timezone transitions.
Schedule and submission defaults remain proposed. Changes must remain explicit in the implementation.
