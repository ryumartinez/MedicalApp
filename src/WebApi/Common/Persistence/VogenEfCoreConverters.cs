using MedicalApp.Common.Domain.Heroes;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Common.Persistence;

// TODO: New strongly typed IDs should be registered here

[EfCoreConverter<HeroId>]
[EfCoreConverter<TeamId>]
[EfCoreConverter<MissionId>]
[EfCoreConverter<AppointmentId>]
[EfCoreConverter<DoctorId>]
[EfCoreConverter<PatientId>]
internal sealed partial class VogenEfCoreConverters;
