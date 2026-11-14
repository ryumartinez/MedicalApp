namespace MedicalApp.Features.Accounts.GetAccount;

public record GetAccountResponse(
    Guid Id,
    string Issuer,
    string Subject,
    bool Enabled,
    Guid? DoctorId,
    Guid? PatientId,
    GetAccountResponse.ProviderAccountDto? ProviderAccount,
    IReadOnlyList<string> PendingSteps)
{
    public record ProviderAccountDto(string Username, bool Enabled, bool PasswordSet, IReadOnlyList<string> Roles);
}
