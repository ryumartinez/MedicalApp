# plans-and-scheduling Specification

## Purpose

Assign published forms to patients and define consistent reporting periods, completion states, and closure behavior across entries and reports.

## Requirements

### Requirement: Assign a published form version

An authorized doctor SHALL assign one published form version to one patient with a daily or weekly schedule.
The assignment MUST record its doctor, patient, timezone, local start date, and optional inclusive end date.
Progress tracking MUST remain independent of appointments.
A patient can have multiple plans from different doctors.

#### Scenario: Assign an independent progress plan

- **WHEN** A doctor with an active treatment relationship assigns a published form version to a patient.
- **THEN** The plan records the assignment and schedule without requiring an appointment.
- **AND** Other authorized plans for that patient remain independent.

#### Scenario: Reject a draft form assignment

- **WHEN** A doctor attempts to assign an unpublished form draft.
- **THEN** The API rejects the assignment.

### Requirement: Keep assignment definitions fixed

A plan's doctor, patient, published version, schedule, and timezone SHALL remain fixed after assignment.
A changed assignment MUST use a new plan.
The previous plan MUST end without losing its entries or history.

#### Scenario: Replace an assignment

- **WHEN** A doctor replaces a patient's assignment with another schedule or published version.
- **THEN** A new plan holds the new assignment.
- **AND** The previous plan ends and retains its historical data.

### Requirement: Use local calendar periods

A plan SHALL use a valid IANA timezone identifier and local calendar dates.
A daily period SHALL cover one local date.
A weekly period SHALL cover seven local dates anchored to the plan's local start date.
An optional end date MUST be inclusive and can shorten the final period.

#### Scenario: Anchor a weekly period to the start date

- **WHEN** A weekly plan starts on a local Wednesday.
- **THEN** Its first period covers Wednesday through the following Tuesday rather than an unrelated calendar-week boundary.

#### Scenario: Shorten the final reporting period

- **WHEN** A plan's inclusive end date falls before the usual end of its final weekly period.
- **THEN** The final period includes that date and excludes subsequent local dates.

#### Scenario: Reject an invalid timezone

- **WHEN** An assignment specifies an invalid IANA timezone identifier.
- **THEN** The API rejects the assignment.

### Requirement: Convert period boundaries consistently

Period boundaries SHALL convert from local calendar dates to UTC through the plan's timezone rules.
Conversions MUST account for daylight saving changes with deterministic transition handling.
Fixed 24-hour or 168-hour intervals MUST NOT replace local calendar periods.

#### Scenario: Span a daylight saving change

- **WHEN** A reporting period crosses a daylight saving transition.
- **THEN** Its local dates remain unchanged and its UTC boundaries reflect the timezone transition.

#### Scenario: Handle an ambiguous or invalid local boundary

- **WHEN** Timezone rules make a local period boundary ambiguous or invalid.
- **THEN** The documented transition rule produces the same UTC boundary for entries, due periods, and reports.

### Requirement: Restrict entry creation to started periods

A patient SHALL start entries only after the reporting period begins and while the plan permits draft changes.
Each plan MUST permit at most one entry per reporting period.
Clients MUST NOT bypass the schedule through supplied patient identifiers or timestamps.

#### Scenario: Request an entry before its period starts

- **WHEN** A patient attempts to start an entry for a future reporting period.
- **THEN** The API rejects the attempt and creates no entry for that period.

#### Scenario: Request an already existing period entry

- **WHEN** The patient requests an entry for a reporting period that already has one.
- **THEN** The system preserves one entry for that plan and period.

### Requirement: Permit late submissions on active plans

A patient SHALL be able to submit a past reporting period while its plan remains active.
A late entry MUST retain its original reporting period and actual submission time.
The system MUST distinguish a late submission from an on-time submission.

#### Scenario: Submit a missed reporting period later

- **WHEN** A patient submits a valid entry after its period ends while the plan remains active.
- **THEN** The period becomes submitted late.
- **AND** The entry keeps its original period and actual submission timestamp.

### Requirement: Calculate due and missed periods from the schedule

The API SHALL derive due and missed periods from the schedule without requiring stored empty entries.
A completed period without a submitted entry SHALL count as missed.
An incomplete draft MUST NOT count as submitted.
Future periods and periods after a plan ends MUST NOT count as missed.

#### Scenario: Count an incomplete draft as missed

- **WHEN** A reporting period ends with only an incomplete draft.
- **THEN** The period counts as missed until a valid submission changes its state.

#### Scenario: Exclude unscheduled time from completion

- **WHEN** A caller requests completion information for future dates or dates after the plan ends.
- **THEN** Unscheduled periods contribute no missed count.

### Requirement: Coordinate plan closure with entry changes

Plan closure SHALL stop subsequent draft changes and submissions while preserving existing entries.
Closure MUST coordinate with the submission transaction defined by `patient-progress-tracking/entries-and-submission`.
A concurrent closure MUST NOT permit a new submission after closure commits.
Revoking a doctor's treatment relationship MUST close that doctor's active plans for the patient.
The revocation MUST NOT close plans owned by other doctors.

#### Scenario: Close a plan with historical entries

- **WHEN** An authorized doctor ends a plan.
- **THEN** Existing entries remain available under the applicable access policies.
- **AND** The patient cannot change drafts or make new submissions on the closed plan.

#### Scenario: Race closure against submission

- **WHEN** Plan closure and a submission attempt run concurrently.
- **THEN** The committed result follows a consistent order.
- **AND** A submission does not commit after closure has already committed.

#### Scenario: Close only the affected doctor's plans

- **WHEN** An administrator revokes one doctor's treatment relationship with a patient.
- **THEN** The doctor's plans for that patient close and retain their entries.
- **AND** Plans owned by other doctors remain unchanged.

### Requirement: Detect conflicting plan updates

The API SHALL detect conflicting concurrent plan updates and return `409` without overwriting a committed change.

#### Scenario: End a stale plan revision

- **WHEN** A plan update conflicts with another committed update.
- **THEN** The API returns `409` and preserves the committed plan state.
