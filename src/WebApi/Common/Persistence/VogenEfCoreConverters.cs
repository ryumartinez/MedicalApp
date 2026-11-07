using MedicalApp.Common.Domain.Heroes;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using DoctorId = MedicalApp.Common.Domain.Doctors.DoctorId;
using PatientId = MedicalApp.Common.Domain.Patients.PatientId;

namespace MedicalApp.Common.Persistence;

// TODO: New strongly typed IDs should be registered here

[EfCoreConverter<HeroId>]
[EfCoreConverter<TeamId>]
[EfCoreConverter<MissionId>]
[EfCoreConverter<AppointmentId>]
[EfCoreConverter<DoctorId>]
[EfCoreConverter<PatientId>]
[EfCoreConverter<ApplicationIdentityId>]
[EfCoreConverter<DoctorPatientRelationshipId>]
[EfCoreConverter<ProgressFormId>]
[EfCoreConverter<ProgressFormVersionId>]
[EfCoreConverter<ProgressQuestionId>]
[EfCoreConverter<ProgressQuestionOptionId>]
[EfCoreConverter<ProgressPlanId>]
[EfCoreConverter<ProgressPlanGrantId>]
[EfCoreConverter<ProgressEntryId>]
[EfCoreConverter<MediaAssetId>]
internal sealed partial class VogenEfCoreConverters;
