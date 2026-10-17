namespace MedicalApp.Features.Teams.ExecuteMission;

public record ExecuteMissionRequest(Guid TeamId, string Description);