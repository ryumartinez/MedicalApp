# media-and-storage Specification

## Purpose

Store patient images and videos privately and provide authorized file transfer, validated finalization, and cleanup without losing submitted media.

## Requirements

### Requirement: Keep patient files private

Patient files SHALL remain in private storage.
Application data MUST retain file ownership, entry and question associations, content type, size, lifecycle state, and expiration information.
Anonymous callers MUST NOT retrieve private files without a valid temporary signed URL.

#### Scenario: Request an object without authorization

- **WHEN** A caller requests a private object without an applicable signed URL or storage authorization.
- **THEN** Storage denies access to the object's contents.

#### Scenario: Retain file associations

- **WHEN** A patient requests an upload for an eligible draft question.
- **THEN** The system records the patient, entry, question, object key, and lifecycle metadata for that asset.

### Requirement: Authorize each media operation

Upload requests, upload finalization, and download URL requests SHALL require authorization against the associated entry.
Resource access MUST use the same identity and treatment policies as progress entries.

#### Scenario: Attempt another patient's media operation

- **WHEN** A patient requests an upload, finalization, or download for another patient's entry.
- **THEN** The API denies access and issues no storage URL or finalized asset.

#### Scenario: Download as an authorized doctor

- **WHEN** A doctor with the required plan access and active treatment relationship requests an attachment.
- **THEN** The API can issue a temporary download URL for that attachment.

### Requirement: Upload through temporary staging URLs

An authorized patient SHALL request an upload for a draft entry and an image or video question.
The API MUST generate the object key and issue a temporary signed upload URL for a staging object.
The URL MUST NOT target finalized file content.

#### Scenario: Start an authorized upload

- **WHEN** A patient requests an upload for their eligible draft entry and attachment question.
- **THEN** The API issues a temporary URL for a server-selected staging object and records a pending asset.

#### Scenario: Reject an ineligible upload target

- **WHEN** A caller requests an upload for a submitted entry or a question outside the entry's assigned attachment questions.
- **THEN** The API rejects the upload request.

### Requirement: Validate stored content before readiness

Finalization SHALL validate the stored object, ownership, size, and actual file format against the applicable rules.
A missing, expired, invalid, or unauthorized upload MUST NOT become ready for submission.

#### Scenario: Reject invalid stored content

- **WHEN** Finalization finds a missing object, disallowed format, excessive size, expired upload, or incorrect ownership.
- **THEN** The API does not mark the asset ready.
- **AND** The asset cannot satisfy a submitted attachment answer.

#### Scenario: Validate actual file format

- **WHEN** A staging object's actual format differs from its permitted format despite its declared content type.
- **THEN** Finalization rejects the object.

### Requirement: Preserve the exact validated file

Successful finalization SHALL promote the exact validated content to a separate final object key before the asset becomes ready.
Concurrent staging replacement MUST NOT substitute unvalidated content.
Existing upload URLs MUST NOT alter finalized content.

#### Scenario: Replace staging content during finalization

- **WHEN** A staging object changes while finalization validates and promotes it.
- **THEN** The final file contains the validated content or finalization fails without creating a ready invalid asset.

#### Scenario: Reuse an upload URL after finalization

- **WHEN** A client reuses a still-valid staging upload URL after successful finalization.
- **THEN** The finalized object's content remains unchanged.

### Requirement: Make finalization retries safe

Repeated successful finalization SHALL return the same asset and result.
Storage failures MUST leave the asset unavailable for submission and permit a safe retry.
Retries MUST NOT create duplicate logical assets or change finalized content.

#### Scenario: Retry a completed finalization

- **WHEN** A caller repeats finalization for an already finalized asset.
- **THEN** The API returns the existing asset and finalization result.

#### Scenario: Recover a storage failure

- **WHEN** Storage fails during validation or promotion.
- **THEN** The asset remains unavailable for submission.
- **AND** A retry can finish finalization without a duplicate asset or unvalidated content.

### Requirement: Expose an explicit asset lifecycle

Assets SHALL use pending, ready, attached, and expired lifecycle states.
Only the submission transaction defined by `patient-progress-tracking/entries-and-submission` MUST move a ready asset to attached.
Upload finalization MUST NOT attach an asset to a submitted entry by itself.

#### Scenario: Finalize without submitting

- **WHEN** A patient successfully finalizes an upload but has not submitted the entry.
- **THEN** The asset becomes ready and remains unattached until submission commits.

#### Scenario: Submit a ready attachment

- **WHEN** Entry submission commits with an eligible ready asset.
- **THEN** The entry and asset become submitted and attached through the shared transaction contract.

### Requirement: Expire and clean abandoned uploads safely

The system SHALL expire uploads and clean abandoned staging objects and eligible unattached final objects under configured limits and retention rules.
Cleanup MUST coordinate with submission and MUST preserve attached media.
Upload expiration and cleanup MUST run without requiring patient requests.

#### Scenario: Clean an abandoned upload

- **WHEN** An upload expires and remains eligible for cleanup.
- **THEN** Cleanup removes its abandoned storage objects according to the configured policy.

#### Scenario: Preserve media during a submission race

- **WHEN** Cleanup and submission compete for the same asset.
- **THEN** Their coordinated state changes preserve media attached by a committed submission.

### Requirement: Issue browser-accessible temporary URLs

Signed URLs SHALL grant temporary access only to their intended object.
Local browser uploads MUST use a reachable storage endpoint with the required CORS configuration.
Signed URLs MUST use the endpoint the browser can reach.

#### Scenario: Upload from a browser

- **WHEN** An authorized browser client uses a signed upload URL from the local application.
- **THEN** The browser reaches the storage endpoint and its permitted CORS request succeeds.

#### Scenario: Expire a signed URL

- **WHEN** A signed URL reaches its expiration time.
- **THEN** Storage rejects further requests through that URL.

### Requirement: Initialize Garage before API storage access

Local orchestration SHALL start Garage and initialize its node layout, private bucket, and access key.
Initialization MUST safely reuse existing state.
The API MUST wait for successful initialization and storage readiness rather than container process startup alone.

#### Scenario: Initialize storage for a fresh application

- **WHEN** Local orchestration starts without existing Garage state.
- **THEN** Initialization prepares the layout, bucket, and access key before the API uses storage.

#### Scenario: Retry storage initialization

- **WHEN** Initialization runs again with an existing layout, bucket, and key.
- **THEN** It reuses valid state without duplicate resources or discarded data.

### Requirement: Preserve Garage data across restarts

Local storage SHALL persist both Garage metadata and object data outside the container lifecycle.
An application restart MUST preserve uploaded content and its usable storage configuration.

#### Scenario: Restart after a file upload

- **WHEN** The locally orchestrated application restarts after a successful file submission.
- **THEN** Garage retains its metadata and object bytes.
- **AND** An authorized caller can still retrieve the submitted file.

### Requirement: Restrict storage administration and credentials

Garage's administrative endpoint SHALL remain internal.
API credentials MUST grant only the required bucket operations.
Credentials MUST come from secret configuration rather than committed values.

#### Scenario: Inspect application storage access

- **WHEN** The API receives its local storage connection information.
- **THEN** It receives the required endpoint and scoped credentials through secret configuration.
- **AND** Browser clients receive no administrative credentials or administrative endpoint access.
