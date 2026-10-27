# Patient progress tracking implementation plan

**Status:** Proposed.

Doctors create custom forms and assign them to patients. Patients submit answers on a daily or weekly schedule. Doctors review the answers over time.

The first release supports structured answers, images, and videos. Garage provides private S3-compatible storage, with Aspire for local orchestration from the first implementation milestone.

**1. User workflows**

The doctor creates a draft form, adds questions, and publishes a version. The doctor then assigns that version to a patient with a schedule.

The patient sees the forms due, saves draft answers, and submits an entry. The doctor reviews the patient's history and numeric trends.

Progress tracking is independent of appointments. A patient can have several plans from different doctors.

| Use case | Questions | Schedule | Release |
|---|---|---|---|
| Psychology | "How do you feel today?" and "What is your energy level?" | Daily | First release |
| Physical therapy | Pain level, exercise repetitions, and exercise notes | Weekly | First release |
| Physical therapy with video | A video of the patient doing the exercises, with optional notes | Weekly | First release |

**2. First release scope**

The first release includes:

- Forms owned by a doctor, with editable drafts and immutable published versions
- Text, numeric scale, single-choice, image, and video questions
- Required questions, instructions, display order, and validation rules
- Patient assignments with daily or weekly schedules
- Draft answers and final submissions
- Due periods, missed periods, and completion counts
- Paginated answer history and numeric trends
- Garage storage through Aspire, with persistent data and automatic initialization
- Authorized uploads and downloads through temporary signed URLs
- File validation, upload expiration, and abandoned upload cleanup
- Authentication and access rules for doctors and patients.

Later releases can add reminders, doctor feedback, corrections to submitted entries, conditional questions, and advanced reports.

The first release covers both the psychology example and video-based physical therapy.

**3. Domain model**

| Type | Responsibility | Important data |
|---|---|---|
| `ProgressForm` | Owns a reusable form and its draft | Owner `DoctorId`, title, description, draft revision |
| `ProgressFormVersion` | Preserves a published definition | Form ID, version number, publication timestamp, questions |
| `ProgressQuestion` | Defines one question within a version or draft | Stable question key, type, label, instructions, order, required flag, validation rules |
| `ProgressPlan` | Assigns a published version to one patient | `DoctorId`, `PatientId`, version ID, schedule, timezone, start date, optional end date, ended timestamp |
| `ProgressEntry` | Records answers for one reporting period | Plan ID, period start date, UTC period boundaries, status, submission timestamp |
| `ProgressAnswer` | Stores a typed answer to one question | Entry ID, version-specific question ID, text value, numeric value, option ID, or `MediaAssetId` |
| `MediaAsset` | Tracks an uploaded file and its lifecycle | Patient ID, entry ID, question ID, object key, content type, size, upload status, expiration timestamp |

`ProgressForm` owns its versions and questions. `ProgressEntry` owns its answers. Plans and entries reference related domain objects by typed IDs.

Each question has a stable key across versions. Its database ID identifies the question in one specific version.

Each single-choice option has an identifier and a label. Answers reference the option identifier from the assigned version.

Numeric questions define a minimum, maximum, optional step, and optional endpoint labels. The initial scale can use integer values.

Answers use fields appropriate to the question type. Only one value representation can contain an answer. An omitted optional answer differs from an empty required answer.

Each image or video question accepts one file in the first release. A doctor can add separate attachment questions for several exercises or images.

Media assets have typed IDs and belong in their own domain folder. Domain types reference media assets without depending on Garage or the S3 SDK.

**4. Form lifecycle and customization**

A doctor can edit a draft and publish it as a new version. Publishing validates the whole form and assigns a unique version number.

Published versions remain immutable. Their labels, options, question types, and validation rules preserve the meaning of submitted answers.

A plan references one published version. Publishing a later version does not change existing plans or entries.

The doctor can duplicate a form for a specific patient. The doctor edits the duplicate, publishes it, and assigns it to that patient.

The first release replaces an assignment through a new plan. The old plan ends and retains its history.

Every answer must reference a question in the assigned version. The API rejects unknown questions, duplicate answers, incorrect types, invalid options, and values outside the configured limits.

**5. Schedule rules**

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
- Ending a plan stops further draft changes and submissions. Existing entries remain available.
- The schedule, timezone, patient, doctor, and form version remain fixed after assignment.

The API calculates due and missed periods from the schedule. The first release does not require a background job to create empty entries.

A period is missed after its end if it has no submitted entry. An incomplete draft does not count as a submission.

A later submission changes the period to submitted late. Completion counts include late submissions and report them separately.

Only scheduled periods count toward completion. Future periods and periods after a plan ends do not count as missed.

The implementation must define deterministic boundary handling for timezone transitions. It must use local dates rather than fixed 24-hour or 168-hour intervals.

**6. Entry lifecycle and concurrency**

Entries have two states: `Draft` and `Submitted`.

Drafts can contain incomplete answers. Saving a draft validates supplied values but permits missing required answers.

Submitting an entry validates all required answers against the assigned form version. A submitted entry remains immutable.

Attachment answers must reference ready assets owned by the patient and associated with the same entry and version-specific question.

Submission marks referenced assets attached in the same database transaction. Cleanup must not remove attached assets or race with submission.

Submission operates on the saved draft. Retrying submission returns the existing submitted entry without another write or duplicate entry.

SQL Server enforces a unique constraint on `(ProgressPlanId, PeriodStartDate)`. Concurrent draft creation must resolve the conflict without creating two entries.

Forms, plans, and entries use optimistic concurrency where concurrent changes can conflict. The API returns `409` for conflicting updates.

The final submission transaction must verify that the plan still permits submission. A concurrent plan closure must not permit a submission after closure.

The API uses `TimeProvider` for current time. Clients cannot choose the submission timestamp or bypass the schedule with a supplied patient ID.

**7. Identity and access**

The current template has an audit user service but does not configure an authentication scheme. This feature requires authenticated doctor and patient identities.

The access rules are:

- A doctor can create and edit their own forms.
- A doctor can assign a form only to a patient they are authorized to treat.
- A patient can read assigned forms and manage their own draft entries.
- A patient can submit only to their own active plans.
- A doctor can review entries only for plans they are authorized to access.
- The API derives the acting identity from authenticated claims.
- The API applies access rules to lists, details, history, and summaries.
- The API authorizes upload requests, upload finalization, and download requests against the associated entry.
- Garage buckets remain private. Signed URLs grant temporary access only to the intended object.

The first implementation milestone selects the authentication provider, account mappings, and doctor-patient relationship mechanism.

**8. Repository structure**

The proposed layout follows the existing vertical slice conventions:

```text
src/WebApi/
  Common/
    Domain/
      ProgressForms/
      ProgressPlans/
      ProgressEntries/
      MediaAssets/
    Interfaces/
      IObjectStorage.cs
    Storage/
      Garage/
    Persistence/
      ProgressForms/
      ProgressPlans/
      ProgressEntries/
      MediaAssets/
  Features/
    ProgressForms/
      ProgressFormsGroup.cs
      CreateProgressForm/
      UpdateProgressFormDraft/
      DuplicateProgressForm/
      PublishProgressForm/
      GetProgressForm/
      GetProgressForms/
    ProgressPlans/
      ProgressPlansGroup.cs
      AssignProgressPlan/
      EndProgressPlan/
      GetProgressPlan/
      GetPatientProgressPlans/
    ProgressEntries/
      ProgressEntriesGroup.cs
      GetDueProgressEntries/
      GetProgressEntry/
      SaveProgressEntryDraft/
      SubmitProgressEntry/
      GetProgressHistory/
      GetProgressSummary/
    MediaAssets/
      MediaAssetsGroup.cs
      RequestMediaUpload/
      FinalizeMediaUpload/
      GetMediaDownloadUrl/
tools/
  AppHost/
    Garage/
```

Each use case contains its endpoint, request, response where applicable, validator, and Swagger summary.

Each domain folder contains its related entities, value objects, errors, and specifications. Namespaces mirror their folders.

Entities use static factories, private EF constructors, and property guards. Types that raise domain events use `AggregateRoot<TId>`.

Every new Vogen ID needs registration in `VogenEfCoreConverters.cs`. Queries use aggregate specification classes with static factory methods.

EF configurations define relationships, field limits, concurrency tokens, and indexes. DbSet properties belong in partial `ApplicationDbContext` files.

Database migrations run through the existing migration service. Development seed data includes sample daily and weekly plans.

`IObjectStorage` defines storage operations. Its Garage implementation configures `AWSSDK.S3` through dependency injection.

The AppHost owns local Garage configuration and initialization resources. File cleanup runs as a scheduled hosted service with database coordination.

**9. History and summaries**

History returns reporting periods, submission timestamps, form versions, answers, and draft or submission status. Results support date filters and pagination.

Attachment answers return media identifiers and file metadata. Clients request temporary download URLs after authorization.

Summaries return completed, missed, and late periods, plus numeric values over time. Doctors and patients use the same reporting period definitions.

Numeric trends identify the question and its form version. Different scales or changed question meanings must remain distinguishable.

The first release provides API responses for a future form builder, patient form screen, and doctor progress view. Frontend implementation needs a separate plan.

**10. Implementation milestones**

| Milestone | Work | Completion criteria |
|---|---|---|
| 1. Access, storage, and foundations | Identity mappings, access policies, typed IDs, schedules, Garage through Aspire, persistent volumes, secrets, and initialization | Aspire starts Garage. Initialization is repeatable. Storage survives restarts. Access and schedule tests pass. |
| 2. Forms | Draft creation, updates, duplication, publication, persistence, and all question types | A doctor can publish text, scale, choice, image, and video questions. Later edits preserve published versions. |
| 3. Plans, entries, and media | Assignments, reporting periods, drafts, uploads, validation, submission, downloads, cleanup, and plan closure | A patient can submit structured answers and files with concurrency protection and authorized access |
| 4. History and summaries | Filtered history, attachment access, completion counts, numeric trends, Swagger documentation, and seed data | Both psychology and video-based physical therapy work through the API |

All four milestones form the first release. Garage and Aspire integration belong to milestone 1, before the upload workflow and progress endpoints.

**11. Validation plan**

Unit tests cover question rules, publication history, answer types, required answers, entry transitions, schedule boundaries, and late submissions.

Media tests cover permitted formats, size limits, upload expiration, asset ownership, attachment state, and cleanup eligibility.

Schedule tests cover daily and weekly periods, daylight saving changes, inclusive end dates, and periods shortened by the plan's end date.

SQL Server integration tests cover relationships, migrations, version preservation, unique period constraints, concurrent draft creation, concurrent submission, and plan closure races.

Endpoint tests cover authorization, invalid version references, unknown questions, incorrect answer types, pagination, summaries, and idempotent submission retries.

Storage integration tests use Garage for uploads, finalization, private downloads, missing objects, expired uploads, and failures during finalization.

Concurrency tests cover cleanup against submission, repeated upload finalization, and attempts to overwrite finalized content.

Aspire smoke checks cover initialization, resource readiness, browser-accessible signed URLs, CORS, and persistence after a restart.

Architecture tests enforce the repository conventions. Validation includes the solution build, all test projects, and an EF pending-model-changes check.

Representative acceptance scenarios are:

- A psychologist assigns a daily mood form and sees the patient's submitted answers and energy history.
- A physical therapist assigns a weekly form for pain, repetitions, and exercise notes.
- A physical therapist assigns a weekly video question and reviews the patient's submitted exercise video.
- A patient submits an image answer and an authorized doctor can retrieve it through a temporary URL.
- Required attachment questions reject pending, expired, missing, or unauthorized assets.
- Retrying upload finalization or entry submission preserves the same file and entry.
- Restarting the Aspire application preserves Garage metadata and uploaded files.
- A doctor publishes a new form version without altering an existing plan or its historical answers.
- Two concurrent submissions produce one submitted entry for the period.
- A late submission appears in the correct reporting period.
- A patient cannot read or change another patient's progress.
- A doctor cannot read another doctor's plans without an explicit access grant.

**12. Garage storage and Aspire orchestration**

Garage provides S3-compatible storage from the first release. SQL Server stores file metadata and ownership. Garage stores the file bytes in private buckets.

The initial implementation includes `MediaAsset`, attachment question types, and an `IObjectStorage` interface. The API uses `AWSSDK.S3` with Garage's endpoint and path-style addressing.

Attachment questions use the same schedules and form versions as structured questions. Published versions preserve their attachment rules and permitted formats.

The upload workflow is:

1. Request an upload for an authorized draft entry and attachment question.
2. Upload the file through a temporary signed URL.
3. Finalize the upload through the API.
4. Validate the stored object, ownership, size, and actual file format.
5. Promote the validated content to a separate object key and mark the media asset ready.
6. Submit the entry with a reference to the ready asset.

The API generates object keys. Upload URLs target staging objects and cannot overwrite finalized files.

Finalization must promote the exact validated content. It must account for concurrent replacement of a staging object.

Repeated finalization returns the existing result. Storage failures keep the asset unavailable for submission and permit a safe retry.

The API authorizes downloads and issues temporary signed URLs. Submission accepts only ready assets associated with the correct patient, entry, and question.

Media assets use the states `Pending`, `Ready`, `Attached`, and `Expired`. Only submission can move a ready asset to the attached state.

The storage implementation includes upload expiration, abandoned upload cleanup, file limits, and a retention policy for submitted media.

Cleanup removes expired staging objects and eligible unattached final objects. It preserves submitted media and coordinates its decisions through database state.

The Aspire AppHost manages Garage as a custom container resource from milestone 1. It supplies configuration, persistent volumes, endpoints, and credentials.

Persistent volumes retain both Garage metadata and file data. The implementation pins a documented Garage image version and validates its configuration.

An initialization resource configures the node layout, bucket, and access key. It reuses existing state and remains safe to run after a restart.

The API waits for successful initialization and storage readiness. Process startup alone does not establish storage readiness.

Credentials use Aspire secret parameters or local user secrets. The API receives its storage configuration through the AppHost.

The Garage administrative endpoint remains internal. The API uses credentials scoped to the required bucket operations.

Browser uploads need a browser-accessible S3 endpoint and CORS configuration. Signed URLs must use the endpoint that the browser can reach.

Production storage needs its own durable Garage deployment and backup plan. Local Aspire orchestration alone does not define that deployment.

The Garage container configuration and Aspire APIs need documentation review and runtime validation during milestone 1.

**13. Decisions to resolve during implementation**

The schedule and submission rules in this document are proposed defaults. Changes to those defaults must remain explicit in the implementation.

The remaining implementation decisions are:

- Authentication provider, identity mappings, and doctor-patient relationship management
- Limits for form size, question text, answer text, and choice counts
- Retention and correction rules for submitted entries
- Media size, format, retention, and storage quotas before the upload implementation
- Reminder channels and delivery timing in a later release.
