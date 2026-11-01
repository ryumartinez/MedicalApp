# Patient progress tracking: history and summaries

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestone:** 4.

## Scope and dependencies

Doctors and patients read filtered answer history, completion counts, and numeric trends.
The first release provides API responses. Frontend implementation needs a separate plan.

This capability depends on [identity and access](identity-and-access.md), [form versions](forms-and-publishing.md), [schedules](plans-and-scheduling.md), and [entries](entries-and-submission.md).
Attachment history also depends on [media metadata and authorized downloads](media-and-storage.md).
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.

## History rules

History returns reporting periods, submission timestamps, form versions, answers, and draft or submission status.
Results support date filters and pagination.

Attachment answers return media identifiers and file metadata.
Clients request temporary download URLs after authorization.
The media spec owns the download workflow.

Access policies apply to both lists and individual history records.

## Summary rules

Summaries return completed, missed, and late periods, plus numeric values over time.
Doctors and patients use the same reporting period definitions from the [schedule spec](plans-and-scheduling.md#due-missed-and-late-periods).

Completion counts include late submissions and report them separately.
Future periods and periods after a plan ends do not count as missed.

Numeric trends identify the question and its form version.
Different scales or changed question meanings must remain distinguishable.

## Repository scope

Reporting use cases belong in the existing `src/WebApi/Features/ProgressEntries/` group.
The use case folders are:

- `GetProgressHistory/`
- `GetProgressSummary/`.

Queries use aggregate specification classes with static factory methods.
Milestone 4 includes Swagger documentation and seed data for sample daily and weekly plans.

## Acceptance criteria

The implementation meets these criteria:

- Authorized doctors and patients can read date-filtered, paginated history.
- History preserves form versions, answer meaning, reporting periods, and actual submission timestamps.
- Attachment history returns media identifiers and metadata for authorized download requests.
- Completion counts use schedule definitions consistently and identify late submissions separately.
- Numeric trends distinguish questions and versions with different scales or meanings.
- Seed data supports psychology, structured physical therapy, and video-based physical therapy scenarios.

## Validation

Endpoint tests cover authorization, date filters, pagination, history content, and summaries.
Summary tests cover missed periods, incomplete drafts, late submissions, future periods, and plan closure.
Trend tests cover version and question identification, including changed scales and question meanings.
End-to-end scenarios use daily and weekly plans, structured answers, and image and video attachments.

## Open decisions

This spec adds no separate open decisions.
Reporting uses the proposed schedule, submission, and retention rules from the linked specs.
