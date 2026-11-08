namespace MedicalApp.Features.Accounts.CreatePasswordSetupLink;

public record CreatePasswordSetupLinkResponse(Uri Url, DateTimeOffset ExpiresAt);
