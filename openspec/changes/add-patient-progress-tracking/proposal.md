# Proposal

## Why

Doctors need reusable forms to track patient progress between appointments, including structured answers, images, and exercise videos.
The existing [release plan](../../../specs/patient-progress-tracking.md) describes this unimplemented work but lacks OpenSpec requirements, scenarios, and task tracking.

## What Changes

This change imports the existing proposal into the OpenSpec workflow and preserves its first-release scope:

- Authenticate doctors and patients through Keycloak, with application identity mappings and treatment access policies.
- Let doctors create form drafts, duplicate forms, and publish immutable versions with five question types.
- Assign published versions to patients with daily or weekly schedules and timezone-aware reporting periods.
- Let patients save draft answers and submit immutable entries with retries and concurrency protection.
- Store images and videos in private Garage storage, with authorized uploads, downloads, validation, and cleanup.
- Return filtered history, completion counts, and numeric trends through the API.
- Establish Keycloak, Garage, schedule foundations, and typed IDs in milestone 1 through the existing Aspire application.

The six source documents remain available under `specs/patient-progress-tracking/` for comparison.
This active change owns the proposed requirements, design, and unchecked implementation tasks.
The conversion does not mark features as implemented or create baseline specifications under `openspec/specs/`.
Source defaults remain proposed, and unresolved decisions remain explicit in the design.

Frontend implementation, reminders, feedback, submission corrections, conditional questions, advanced reports, and production deployment plans require separate work.

## Capabilities

### New Capabilities

- `patient-progress-tracking/identity-and-access`: Keycloak authentication, identity mappings, provisioning, treatment relationships, and resource authorization.
- `patient-progress-tracking/forms-and-publishing`: Reusable forms, typed questions, draft changes, duplication, and immutable publications.
- `patient-progress-tracking/plans-and-scheduling`: Assignments, daily and weekly periods, timezones, due dates, late periods, and plan closure.
- `patient-progress-tracking/entries-and-submission`: Draft answers, version validation, immutable submission, idempotency, and the shared submission transaction.
- `patient-progress-tracking/media-and-storage`: Private Garage storage, Aspire initialization, signed URLs, finalization, expiration, and cleanup.
- `patient-progress-tracking/history-and-summaries`: Authorized history, attachment metadata, completion counts, and version-aware numeric trends.

### Modified Capabilities

None. The project has no existing OpenSpec baseline capabilities.

## Impact

Future implementation adds vertical slices for forms, plans, entries, and media, plus shared authentication and authorization infrastructure.
It extends the existing doctor and patient domain through typed references, PostgreSQL persistence, EF migrations, and development seed data.

Dependencies include the official Aspire Keycloak hosting and authentication integrations, `AWSSDK.S3`, and a pinned Garage container.
The AppHost gains persistent resources and initialization dependencies.
Integration fixtures gain real Keycloak and Garage coverage alongside the existing PostgreSQL, Testcontainers, and Respawn setup.

The implementation must preserve the current VSA conventions and migration service.
Each new progress endpoint requires authentication and resource access policies.
