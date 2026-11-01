# Patient progress tracking: plans and scheduling

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestones:** 1 (schedule foundations) and 3 (assignments and plan closure).

## Scope and dependencies

A progress plan assigns one published form version to one patient with a daily or weekly schedule.
Progress tracking is independent of appointments. A patient can have several plans from different doctors.

Assignments depend on [identity and access](identity-and-access.md) and [published forms](forms-and-publishing.md).
Schedule rules form a foundation for [entries](entries-and-submission.md) and [summaries](history-and-summaries.md).
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.

## Domain model

`ProgressPlan` stores `DoctorId`, `PatientId`, the form version ID, schedule, timezone, local start date, optional end date, and ended timestamp.
Plans reference related domain objects through typed IDs.

The schedule, timezone, patient, doctor, and form version remain fixed after assignment.
The first release replaces an assignment through a new plan.
The old plan ends and retains its history.

## Schedule rules

The proposed first release uses these rules:

- A plan stores a valid IANA timezone identifier and local start date.
- A daily period covers one local date.
- A weekly period covers seven local dates, anchored to the plan's start date.
- The optional end date is inclusive. It can shorten the final period.
- Local period boundaries convert to UTC with timezone rules, including daylight saving changes.
- A patient can start an entry after its reporting period begins.
- Each plan permits one entry per reporting period.
- A patient can submit a late entry while the plan remains active.
- A late entry retains its original reporting period and its actual submission timestamp.

The implementation must define deterministic boundary handling for timezone transitions.
It must use local dates rather than fixed 24-hour or 168-hour intervals.
The API uses `TimeProvider` for current time.

## Due, missed, and late periods

The API calculates due and missed periods from the schedule.
The first release does not require a background job to create empty entries.

If a period ends without a submitted entry, it is missed.
An incomplete draft does not count as a submission.

A later submission changes the period to submitted late.
Completion counts include late submissions and report them separately.

Only scheduled periods count toward completion.
Future periods and periods after a plan ends do not count as missed.
The [history spec](history-and-summaries.md) uses these definitions for reports.

## Plan closure and submission

Plan closure stops further draft changes and submissions. Existing entries remain available.

The [submission transaction contract](entries-and-submission.md#submission-transaction-contract) owns concurrency rules between plan closure and submission.
The plan implementation must participate in that contract.

## Repository scope

Domain types belong in `src/WebApi/Common/Domain/ProgressPlans/`.
EF configurations belong in `src/WebApi/Common/Persistence/ProgressPlans/`.

The feature group is `src/WebApi/Features/ProgressPlans/ProgressPlansGroup.cs`.
The use case folders are:

- `AssignProgressPlan/`
- `EndProgressPlan/`
- `GetProgressPlan/`
- `GetPatientProgressPlans/`.

## Acceptance criteria

The implementation meets these criteria:

- An authorized doctor can assign a published version with a daily or weekly schedule.
- Period boundaries use the plan's local dates and timezone.
- An inclusive end date can shorten the final period.
- Missed and late periods use the same definitions for entries and reports.
- A replacement plan preserves the old plan and its history.
- Plan closure stops draft changes and submissions, including concurrent submission attempts.
- Conflicting plan updates return `409`.

## Validation

Unit tests cover daily and weekly periods, late submissions, and daylight saving changes.
Schedule tests cover inclusive end dates and periods shortened by the plan's end date.
PostgreSQL integration tests cover relationships, migrations, and concurrent plan closure with submission.
Endpoint tests cover treatment authorization, published version references, schedule restrictions, and plan closure.

## Open decisions

The schedule implementation must define deterministic handling for timezone transitions.
Schedule and submission defaults remain proposed. Changes must remain explicit in the implementation.
