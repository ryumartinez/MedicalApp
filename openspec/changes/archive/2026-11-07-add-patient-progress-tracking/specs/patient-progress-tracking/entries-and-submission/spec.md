# Spec Delta

## Purpose

Let patients save typed draft answers and submit immutable entries once per reporting period, with safe retries and coordinated attachment handling.

## ADDED Requirements

### Requirement: Save incomplete draft answers

Patients SHALL save draft answers for their own permitted reporting periods.
Draft saves MUST validate supplied values but permit missing required answers.
The system MUST distinguish an omitted optional answer from an empty required answer.
Entries SHALL expose draft or submitted status.

#### Scenario: Save a partial draft

- **WHEN** A patient saves valid answers while other required questions remain unanswered.
- **THEN** The API saves the draft without marking it submitted.

#### Scenario: Reject an invalid supplied draft value

- **WHEN** A draft contains a supplied value that violates its question's rules.
- **THEN** The API rejects that invalid value rather than treating draft status as a validation exemption.

### Requirement: Match answers to the assigned version

Each answer SHALL reference a question in the plan's assigned published form version.
The API MUST reject unknown questions, duplicate answers, incorrect types, invalid options, and values outside configured limits.
Single-choice answers MUST use an option identifier from that version.

#### Scenario: Reject a question from another version

- **WHEN** A patient saves an answer referencing a question outside the assigned version.
- **THEN** The API rejects the answer.

#### Scenario: Reject duplicate or invalid answers

- **WHEN** A request includes duplicate question answers, an incorrect answer type, an invalid option, or an out-of-range value.
- **THEN** The API rejects the invalid request.

### Requirement: Store one representation per answer

An answer SHALL use only the value representation appropriate to its question type.
It MUST NOT contain multiple simultaneous text, numeric, option, or media values.
Attachment answers MUST identify one asset for an image or video question.

#### Scenario: Reject mixed answer representations

- **WHEN** An answer supplies both a numeric value and a media asset, or another incompatible combination.
- **THEN** The API rejects the answer.

### Requirement: Submit the saved draft after complete validation

Submission SHALL operate on the saved draft and validate every required answer against the assigned version.
It MUST verify that the patient and reporting period remain eligible under the plan.
The server MUST assign the actual submission time.
Clients MUST NOT override the acting patient or submission timestamp.

#### Scenario: Submit a valid saved draft

- **WHEN** A patient submits a saved draft with all required valid answers for a permitted reporting period.
- **THEN** The entry becomes submitted with the server's actual submission timestamp.

#### Scenario: Reject missing required answers

- **WHEN** A patient submits a draft with a missing or empty required answer.
- **THEN** The API rejects submission and leaves the entry unsubmitted.

#### Scenario: Preserve the period for a late submission

- **WHEN** A patient submits a past reporting period while its plan remains active.
- **THEN** The entry retains that reporting period and records the actual later submission time.

### Requirement: Keep submitted entries immutable and retries idempotent

Submitted entries SHALL remain immutable in the first release.
An authorized submission retry MUST return the existing submitted entry without another write or duplicate entry.

#### Scenario: Retry a successful submission

- **WHEN** An authorized patient retries submission after the first submission already committed.
- **THEN** The API returns the same submitted entry and submission timestamp without another write.

#### Scenario: Attempt to edit a submitted entry

- **WHEN** A patient attempts to save changes to a submitted entry.
- **THEN** The API rejects the change and preserves the submitted answers.

### Requirement: Preserve one entry per plan and period

The system SHALL enforce uniqueness for each plan and reporting period start date.
Concurrent draft creation MUST NOT create multiple entries.
Concurrent submissions MUST produce at most one submitted entry for the period.

#### Scenario: Create drafts concurrently

- **WHEN** Two requests create a draft for the same plan and reporting period.
- **THEN** The system preserves one entry and resolves the competing creation without storing a duplicate.

#### Scenario: Submit concurrently

- **WHEN** Two requests submit the same reporting period concurrently.
- **THEN** At most one submission transition commits.
- **AND** A successful retry returns the existing submitted entry.

### Requirement: Validate attachment eligibility at submission

Each attachment SHALL reference a ready asset owned by the submitting patient.
The asset MUST belong to the same entry and version-specific question.
Pending, expired, missing, or unauthorized assets MUST NOT satisfy an attachment answer.

#### Scenario: Reject an ineligible required attachment

- **WHEN** A required attachment references a pending, expired, missing, or unauthorized asset.
- **THEN** Submission fails and the entry remains unsubmitted.

#### Scenario: Reject an asset for another entry or question

- **WHEN** A patient references an otherwise ready asset associated with another entry or version-specific question.
- **THEN** The API rejects submission with that asset.

### Requirement: Coordinate submission with plan closure

The final submission transaction SHALL verify that the plan still permits submission.
Plan closure and submission MUST coordinate their committed order.
A closure that commits first MUST prevent the new submission from committing afterward.

#### Scenario: Close the plan before submission commits

- **WHEN** A concurrent plan closure commits before the submission transaction can complete.
- **THEN** The new submission does not commit.
- **AND** No attachment transition from that failed submission commits.

### Requirement: Attach assets atomically with submission

Submission SHALL mark referenced ready assets as attached in the same database transaction as the entry's submitted state.
Only submission MUST move a ready asset to attached.
Cleanup MUST coordinate with that transaction and MUST NOT remove attached assets.

#### Scenario: Commit the entry and attachments together

- **WHEN** A valid submission with ready assets commits.
- **THEN** The entry becomes submitted and its referenced assets become attached together.

#### Scenario: Roll back a failed submission

- **WHEN** The database transaction fails before submission commits.
- **THEN** Neither the submitted entry state nor the attachment state changes remain committed.

#### Scenario: Race cleanup against submission

- **WHEN** Cleanup and submission compete for the same eligible asset.
- **THEN** Their coordinated state changes prevent deletion of an asset committed as attached.
- **AND** Submission does not succeed with an asset that cleanup already made ineligible.

### Requirement: Detect conflicting draft changes

The API SHALL detect conflicting concurrent entry changes and return `409` without overwriting committed answers.
This conflict behavior MUST preserve the idempotent submission retry contract.

#### Scenario: Save a stale draft revision

- **WHEN** A draft update conflicts with another committed update.
- **THEN** The API returns `409` and preserves the committed draft.

#### Scenario: Retry an already submitted entry after a conflict

- **WHEN** An authorized caller retries submission after another request already committed it.
- **THEN** The API returns the existing submitted entry under the retry contract.
