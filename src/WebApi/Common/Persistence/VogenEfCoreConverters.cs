using MedicalApp.Common.Domain.Heroes;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Common.Domain.Appointments;
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
internal sealed partial class VogenEfCoreConverters;
