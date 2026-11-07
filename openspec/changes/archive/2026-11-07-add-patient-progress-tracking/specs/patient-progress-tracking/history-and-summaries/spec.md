# Spec Delta

## Purpose

Provide authorized answer history and progress summaries that preserve form meaning and use consistent reporting period definitions.

## ADDED Requirements

### Requirement: Return filtered and paginated history

Authorized doctors and patients SHALL read answer history through the API with date filters and pagination.
History MUST include reporting periods, actual submission timestamps, form versions, answers, and draft or submitted status.
Lists and individual records MUST apply the same resource access policies.

#### Scenario: Read a page within a date range

- **WHEN** An authorized caller requests history with date filters and pagination.
- **THEN** The API returns the permitted page of matching records with their periods, versions, answers, timestamps, and status.

#### Scenario: Restrict history by current access

- **WHEN** A caller requests history outside their permitted patient or plan scope.
- **THEN** Collection responses contain no unauthorized records.
- **AND** Detail requests follow the identity capability's `404` response rule.

### Requirement: Return attachment identifiers and metadata

Attachment history SHALL return media identifiers and file metadata.
Clients MUST request temporary download URLs through the authorized media workflow.
History MUST NOT bypass that workflow through public storage access.

#### Scenario: Read a submitted exercise video

- **WHEN** An authorized doctor reads history containing a video answer.
- **THEN** The history identifies the media asset and its file metadata.
- **AND** The doctor can request a temporary URL through the separately authorized download operation.

### Requirement: Calculate completion counts consistently

Summaries SHALL report completed, missed, and late periods through the schedule capability's definitions.
Completed counts MUST include late submissions and identify them separately.
Incomplete drafts MUST NOT count as completed.
Future periods and periods after a plan ends MUST NOT count as missed.

#### Scenario: Count a late submission

- **WHEN** A patient submits a previously missed reporting period while the plan remains active.
- **THEN** Its completion count includes that submission and the late count identifies it separately.
- **AND** The period no longer contributes to the missed count.

#### Scenario: Exclude drafts and unscheduled periods

- **WHEN** A summary covers incomplete drafts, future periods, or dates after plan closure.
- **THEN** Drafts contribute no completed count.
- **AND** Future and post-plan periods contribute no missed count.

### Requirement: Preserve question and version meaning in trends

Numeric trends SHALL identify each question and its form version.
Different scales and changed question meanings MUST remain distinguishable.
Later form publications MUST NOT reinterpret historical answers through new definitions.

#### Scenario: Change a scale in a later version

- **WHEN** A later published form changes a numeric scale or question meaning.
- **THEN** Trend responses distinguish the question versions and preserve the original interpretation of each historical value.

### Requirement: Support first-release clinical workflows

The API SHALL support daily psychology tracking, weekly structured physical therapy, and weekly exercise video tracking.
Development seed data MUST demonstrate daily and weekly plans and these workflows.
Frontend implementation remains separate.

#### Scenario: Review daily mood and energy

- **WHEN** A psychologist assigns a daily mood and energy form and the patient submits answers.
- **THEN** The psychologist can review the permitted answer history and energy values over time.

#### Scenario: Review structured physical therapy

- **WHEN** A physical therapist assigns weekly pain, repetition, and exercise note questions and the patient submits answers.
- **THEN** The therapist can review the permitted weekly history and numeric trends.

#### Scenario: Review video-based physical therapy

- **WHEN** A patient submits a validated exercise video for a weekly plan.
- **THEN** The authorized therapist can find the submission in history and request a temporary download URL.

#### Scenario: Review an image answer

- **WHEN** A patient submits a validated image answer.
- **THEN** An authorized doctor can find its media metadata and retrieve it through a temporary download URL.
