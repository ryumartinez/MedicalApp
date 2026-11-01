# Media and storage Specification

## Purpose

Define private image and video storage, authorized temporary URLs, immutable finalized content, and coordinated media cleanup through Garage.

## Status and source

**Status:** Proposed. These requirements describe intended behavior, not completed implementation.

Source: [Media and storage](../../../../specs/patient-progress-tracking/media-and-storage.md).
Parent: [Patient progress tracking](../spec.md).
Milestones: 1 for Garage and Aspire foundations and 3 for media workflows.
Uploads depend on [Identity](../identity-and-access/spec.md), [Attachment questions](../forms-and-publishing/spec.md), and [Draft entries](../entries-and-submission/spec.md).
Media limits, permitted formats, expiration, cleanup timing, retention, and quotas remain open decisions.

## Requirements

### Requirement: Private storage and metadata separation
Garage MUST store file bytes in private S3-compatible buckets.
PostgreSQL MUST store media metadata and ownership.
`MediaAsset` MUST retain a typed ID, patient, entry, question, object key, content type, size, status, and expiration timestamp.

#### Scenario: Persist a media asset
- **WHEN** the API creates an asset for an authorized attachment question
- **THEN** PostgreSQL records its metadata and ownership while Garage stores the file bytes privately.

### Requirement: Storage abstraction
Domain types MUST reference media assets without dependencies on Garage or the S3 SDK.
`IObjectStorage` MUST define storage operations.
Its Garage implementation MUST configure `AWSSDK.S3` through dependency injection with the Garage endpoint and path-style addressing.

#### Scenario: Domain model references an attachment
- **WHEN** a progress answer references a media asset
- **THEN** the domain uses its typed asset ID without a Garage or S3 SDK dependency.

### Requirement: Authorized staging uploads
Upload requests MUST authorize a draft entry and its attachment question before returning a temporary signed upload URL.
The API MUST generate object keys.
Upload URLs MUST target staging objects and MUST NOT overwrite finalized files.

#### Scenario: Upload an image answer
- **WHEN** a patient requests an upload for their authorized draft entry and image question
- **THEN** the API returns a temporary URL for an API-generated staging key.

#### Scenario: Attempt to overwrite finalized content
- **WHEN** a caller reuses a staging upload URL after finalization
- **THEN** that URL cannot change the finalized file content.

### Requirement: Validation of stored content
Finalization MUST authorize access to the associated entry and validate the stored object, ownership, size, and actual file format.
Only validated content MUST become ready for submission.

#### Scenario: Invalid stored file
- **WHEN** a stored object is missing or violates ownership, size, or actual-format rules
- **THEN** finalization does not make the asset ready for submission.

### Requirement: Exact-content promotion
Finalization MUST promote the exact validated content to a separate object key.
Concurrent replacement of the staging object MUST NOT substitute unvalidated content for the finalized file.

#### Scenario: Staging object changes during finalization
- **WHEN** a caller replaces a staging object concurrently with validation and promotion
- **THEN** finalization preserves the exact validated bytes or fails without making substituted content ready.

### Requirement: Idempotent and retry-safe finalization
Repeated finalization MUST return the existing result for the same asset.
Storage failures MUST keep the asset unavailable for submission and permit a safe retry.

#### Scenario: Repeated finalization
- **WHEN** a caller repeats successful finalization, including concurrent requests
- **THEN** the API returns the same asset and finalized content.

#### Scenario: Storage failure during finalization
- **WHEN** storage fails before finalization succeeds
- **THEN** the asset remains unavailable for submission and a retry can safely finish finalization.

### Requirement: Authorized temporary downloads
Download requests MUST authorize access against the associated entry before issuing temporary signed URLs.
Buckets MUST remain private and signed URLs MUST grant access only to the intended object.

#### Scenario: Authorized attachment review
- **WHEN** an authorized doctor requests a submitted image or video
- **THEN** the API returns a temporary signed URL for that object.

#### Scenario: Unauthorized object request
- **WHEN** a caller lacks entry access or attempts direct unauthenticated bucket access
- **THEN** the API or private bucket denies access.

### Requirement: Media lifecycle and submission ownership
Assets MUST use `Pending`, `Ready`, `Attached`, and `Expired` states.
Only entry submission MUST move a ready asset to attached under the [submission transaction contract](../entries-and-submission/spec.md#submission-transaction-contract).

#### Scenario: Finalization without submission
- **WHEN** file validation and promotion succeed
- **THEN** the asset becomes ready and remains unattached until entry submission.

#### Scenario: Attach an eligible asset
- **WHEN** submission validates an asset's patient, entry, question, and ready state
- **THEN** the submission transaction marks the asset attached with the submitted entry.

### Requirement: Expiration and coordinated cleanup
Media storage MUST include upload expiration, file limits, submitted-media retention policy, and abandoned upload cleanup.
A scheduled hosted service MUST coordinate cleanup through database state.
Cleanup MUST remove expired staging objects and eligible unattached final objects while preserving submitted media.

#### Scenario: Abandoned upload
- **WHEN** an upload expires or an unattached final object becomes eligible under the cleanup policy
- **THEN** cleanup removes the eligible object through its coordinated database decision.

#### Scenario: Cleanup races with submission
- **WHEN** cleanup and submission compete for a ready asset
- **THEN** cleanup follows the submission contract and does not remove attached media.

### Requirement: Aspire-managed Garage foundations
From milestone 1, the AppHost MUST manage Garage as a custom container with configuration, endpoints, credentials, and persistent volumes.
Volumes MUST preserve both Garage metadata and file data.
The implementation MUST pin a documented Garage image and validate its configuration and Aspire APIs through documentation and runtime checks.

#### Scenario: Restart with stored media
- **WHEN** the Aspire application restarts after storing an attachment
- **THEN** Garage retains the attachment bytes and metadata through persistent volumes.

### Requirement: Repeatable Garage initialization and readiness
An initialization resource MUST configure Garage's node layout, bucket, and access key while reusing existing state safely.
The API MUST wait for successful initialization and storage readiness, beyond process startup.

#### Scenario: Repeat initialization
- **WHEN** initialization runs after a restart with an existing layout, bucket, and key
- **THEN** it reuses that state without duplicate or lost storage configuration.

#### Scenario: Process running before storage readiness
- **WHEN** Garage starts but initialization or storage readiness remains incomplete
- **THEN** the API waits before it treats storage as available.

### Requirement: Restricted storage configuration
Credentials MUST use Aspire secret parameters or local user secrets.
The AppHost MUST supply API storage configuration.
The Garage administrative endpoint MUST remain internal and API credentials MUST cover only required bucket operations.

#### Scenario: Storage credential provisioning
- **WHEN** local orchestration supplies Garage credentials to the API
- **THEN** secrets remain outside committed configuration and the API receives only the required bucket permissions.

### Requirement: Browser-accessible signed URLs
Browser uploads MUST use a browser-accessible S3 endpoint and CORS configuration.
Signed URLs MUST use the endpoint that the browser can reach.

#### Scenario: Browser direct upload
- **WHEN** a browser uses an authorized signed upload URL from the API
- **THEN** it can reach the S3 endpoint and the CORS configuration permits the intended upload.

### Requirement: Separate production storage deployment
Production storage MUST have a durable Garage deployment and backup plan separate from local Aspire orchestration.

#### Scenario: Production deployment planning
- **WHEN** the application prepares for production deployment
- **THEN** its deployment plan defines durable Garage storage and backups beyond the local AppHost configuration.

## Repository scope

The implementation uses these paths:

- `src/WebApi/Common/Domain/MediaAssets/`
- `src/WebApi/Common/Persistence/MediaAssets/`
- `src/WebApi/Common/Interfaces/IObjectStorage.cs`
- `src/WebApi/Common/Storage/Garage/`
- `tools/AppHost/Garage/`.

The feature group is `src/WebApi/Features/MediaAssets/MediaAssetsGroup.cs`.
The use case folders are:

- `RequestMediaUpload/`
- `FinalizeMediaUpload/`
- `GetMediaDownloadUrl/`.

## Validation

Media unit tests cover permitted formats, size limits, upload expiration, asset ownership, attachment state, and cleanup eligibility.
PostgreSQL integration tests cover media relationships and migrations.
Storage integration tests use Garage for uploads, finalization, private downloads, missing objects, expired uploads, and failures during finalization.
Concurrency tests cover cleanup against submission, repeated upload finalization, staging replacement, and attempts to overwrite finalized content.
Endpoint tests cover authorization for upload requests, finalization, and downloads.
Aspire smoke checks cover initialization, resource readiness, browser-accessible signed URLs, CORS, and persistence after a restart.

## Open decisions

These decisions need resolution before the upload implementation:

- Media size limits and permitted formats
- Upload expiration and cleanup timing
- Media retention and storage quotas.

Milestone 1 selects and validates the pinned Garage image version and container configuration.
Production deployment and backups need a separate deployment plan.
