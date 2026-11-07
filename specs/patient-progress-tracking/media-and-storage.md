# Patient progress tracking: media and storage

**Status:** Proposed.
**Parent:** [Patient progress tracking](../patient-progress-tracking.md).
**Milestones:** 1 (Garage and Aspire foundations) and 3 (media workflows).

## Scope and dependencies

Garage provides private S3-compatible storage for image and video answers in the first release.
PostgreSQL stores file metadata and ownership. Garage stores the file bytes in private buckets.

Garage and Aspire integration belong to milestone 1, before the upload workflow and progress endpoints.
The upload workflow depends on [identity and access](identity-and-access.md), [attachment question definitions](forms-and-publishing.md), and [draft entries](entries-and-submission.md).
The [parent spec](../patient-progress-tracking.md) defines shared repository conventions and release validation.

## Domain and storage boundaries

`MediaAsset` tracks an uploaded file and its lifecycle.
It stores the patient ID, entry ID, question ID, object key, content type, size, upload status, and expiration timestamp.

Media assets have typed IDs and belong in their own domain folder.
Domain types reference media assets without dependencies on Garage or the S3 SDK.

The initial implementation includes `MediaAsset`, attachment question types, and `IObjectStorage`.
`IObjectStorage` defines storage operations.
Its Garage implementation configures `AWSSDK.S3` through dependency injection, with Garage's endpoint and path-style addressing.

The [forms spec](forms-and-publishing.md) owns attachment question definitions, including the one-file limit and immutable rules for published versions.

## Upload and download workflow

The upload workflow has six steps:

1. Request an upload for an authorized draft entry and attachment question.
2. Upload the file through a temporary signed URL.
3. Finalize the upload through the API.
4. Validate the stored object, ownership, size, and actual file format.
5. Promote the validated content to a separate object key. Mark the media asset ready.
6. Submit the entry with a reference to the ready asset.

The API generates object keys.
Upload URLs target staging objects and cannot overwrite finalized files.

Finalization must promote the exact validated content.
It must account for concurrent replacement of a staging object.

Repeated finalization returns the existing result.
Storage failures keep the asset unavailable for submission and permit a safe retry.

The API authorizes upload requests, finalization, and downloads against the associated entry.
The API issues temporary signed URLs for authorized downloads.
Garage buckets remain private. Signed URLs grant temporary access only to the intended object.

## Asset lifecycle and cleanup

Media assets use the states `Pending`, `Ready`, `Attached`, and `Expired`.

The [submission transaction contract](entries-and-submission.md#submission-transaction-contract) owns asset eligibility, attachment state changes, and coordination with cleanup.
Only submission can move a ready asset to the attached state.

The storage implementation includes upload expiration, abandoned upload cleanup, file limits, and a retention policy for submitted media.
File cleanup runs as a scheduled hosted service with database coordination.

Cleanup removes expired staging objects and eligible unattached final objects.
It preserves submitted media and coordinates its decisions through database state.
Cleanup must obey the submission transaction contract.

## Garage and Aspire foundations

The Aspire AppHost manages Garage as a custom container resource from milestone 1.
It supplies configuration, persistent volumes, endpoints, and credentials.
The AppHost owns local Garage configuration and initialization resources.

Persistent volumes retain both Garage metadata and file data.
The implementation pins a documented Garage image version and validates its configuration.

An initialization resource configures the node layout, bucket, and access key.
It reuses existing state and remains safe to run after a restart.

The API waits for successful initialization and storage readiness.
Process startup alone does not establish storage readiness.

Credentials use Aspire secret parameters or local user secrets.
The API receives its storage configuration through the AppHost.

The Garage administrative endpoint remains internal.
The API uses credentials scoped to the required bucket operations.

Browser uploads need a browser-accessible S3 endpoint and CORS configuration.
Signed URLs must use the endpoint that the browser can reach.

Production storage needs its own durable Garage deployment and backup plan.
Local Aspire orchestration alone does not define that deployment.

The Garage container configuration and Aspire APIs need documentation review and runtime validation during milestone 1.

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

## Acceptance criteria

The implementation meets these criteria:

- Aspire starts Garage with repeatable initialization and persistent metadata and file data.
- The API waits for storage readiness and receives the required configuration and credentials.
- Browser-accessible signed URLs support authorized image and video uploads and downloads.
- Finalization validates and preserves the exact file content, including during concurrent staging replacement.
- Repeated finalization returns the same asset, and finalized content cannot change through an upload URL.
- Storage failures permit safe finalization retries and keep invalid assets unavailable for submission.
- Cleanup removes eligible abandoned objects and preserves submitted media.

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
