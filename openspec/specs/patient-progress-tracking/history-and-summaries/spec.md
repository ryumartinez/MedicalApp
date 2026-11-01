# History and summaries Specification

## Purpose

Define authorized answer history, completion counts, and version-aware numeric trends for doctors and patients through the API.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [History and summaries](../../../../specs/patient-progress-tracking/history-and-summaries.md).
Parent: [Patient progress tracking](../spec.md).
Milestone: 4.
Dependencies: [Identity](../identity-and-access/spec.md), [Forms](../forms-and-publishing/spec.md), [Plans](../plans-and-scheduling/spec.md), [Entries](../entries-and-submission/spec.md), and [Media](../media-and-storage/spec.md).
Schedule, submission, and retention defaults remain as proposed in those specs.

## Requirements

### Requirement: Authorized filtered and paginated history
Authorized doctors and patients MUST be able to read answer history with date filters and pagination.
Lists and individual history records MUST enforce the same access policies.

#### Scenario: Filter and paginate permitted history
- **WHEN** an authorized caller requests history with a date range and pagination
- **THEN** the API returns the matching page within the caller's permitted scope.

#### Scenario: Another patient's history
- **WHEN** a patient requests another patient's history
- **THEN** the API denies detail access or excludes out-of-scope results under the identity response rules.

### Requirement: Historical answer context
History MUST return reporting periods, actual submission timestamps, form versions, answers, and draft or submitted status.
Historical answers MUST retain the meaning of their assigned form version.

#### Scenario: Late answer history
- **WHEN** a caller reads a late submission after a newer form version exists
- **THEN** the result preserves its original period, actual submission timestamp, and original form version.

### Requirement: Attachment history and downloads
Attachment answers in history MUST return media identifiers and file metadata.
Clients MUST request temporary download URLs through the authorized [Media workflow](../media-and-storage/spec.md).

#### Scenario: Review an exercise video
- **WHEN** an authorized therapist reads history with a submitted video answer
- **THEN** the history includes the video's media identifier and metadata.
- **AND** the therapist can request a temporary download URL through the authorized media endpoint.

### Requirement: Schedule-consistent completion counts
Summaries MUST return completed, missed, and late periods under the definitions in [Plans and scheduling](../plans-and-scheduling/spec.md).
Completion counts MUST include late submissions and report them separately.

#### Scenario: Late submission changes completion
- **WHEN** a previously missed period receives an eligible late submission
- **THEN** the summary includes it in completion counts and identifies it as late.

### Requirement: Exclusion of drafts and ineligible missed periods
An incomplete draft MUST NOT count as a submission.
Future periods and periods after plan closure MUST NOT count as missed.

#### Scenario: Summary with incomplete and future periods
- **WHEN** a plan has an ended period with only a draft and other future periods
- **THEN** the ended period counts as missed and the future periods do not.

#### Scenario: Plan ends before later periods
- **WHEN** a summary calculates missed periods after plan closure
- **THEN** it excludes periods after that plan ends.

### Requirement: Version-aware numeric trends
Numeric trends MUST return numeric values over time with question and form version identifiers.
Different scales and changed question meanings MUST remain distinguishable.

#### Scenario: Scale changes across versions
- **WHEN** numeric history includes questions with different scales or meanings across published versions
- **THEN** trend responses retain enough question and version information to distinguish those definitions.

### Requirement: Documented API and representative seeds
Milestone 4 MUST include Swagger documentation and sample daily and weekly plans.
Seed data MUST support psychology, structured physical therapy, and video-based physical therapy scenarios.
Frontend implementation MUST require a separate plan.

#### Scenario: Demonstrate first release workflows
- **WHEN** development seed data initializes and a developer uses the documented API
- **THEN** daily and weekly workflows demonstrate structured answers and image or video attachments without a frontend requirement.

## Repository scope

Reporting use cases belong in the existing `src/WebApi/Features/ProgressEntries/` group.
The use case folders are:

- `GetProgressHistory/`
- `GetProgressSummary/`.

Queries use aggregate specification classes with static factory methods.
Milestone 4 includes Swagger documentation and seed data for sample daily and weekly plans.

## Validation

Endpoint tests cover authorization, date filters, pagination, history content, and summaries.
Summary tests cover missed periods, incomplete drafts, late submissions, future periods, and plan closure.
Trend tests cover version and question identification, including changed scales and question meanings.
End-to-end scenarios use daily and weekly plans, structured answers, and image and video attachments.

## Open decisions

This spec adds no separate open decisions.
Reporting uses the proposed schedule, submission, and retention rules from the linked specs.
