namespace MedicalApp.Features.Teams.AddHeroToTeam;

public record AddHeroToTeamRequest(Guid TeamId, Guid HeroId);