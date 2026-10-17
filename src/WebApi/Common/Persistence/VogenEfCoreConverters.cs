using MedicalApp.Common.Domain.Heroes;
using MedicalApp.Common.Domain.Teams;

namespace MedicalApp.Common.Persistence;

// TODO: New strongly typed IDs should be registered here

[EfCoreConverter<HeroId>]
[EfCoreConverter<TeamId>]
[EfCoreConverter<MissionId>]
internal sealed partial class VogenEfCoreConverters;