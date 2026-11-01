using MedicalApp.Common.Authentication;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts;

/// <summary>
/// Lists the provisioning steps an account still needs. An empty list means the account is fully provisioned and enabled.
/// </summary>
public static class AccountProvisioningState
{
    public const string ProviderAccountMissing = "provider-account-missing";
    public const string ProviderAccountDisabled = "provider-account-disabled";
    public const string RecordLinkMissing = "record-link-missing";
    public const string DoctorLinkMissing = "doctor-link-missing";
    public const string DoctorRoleMissing = "doctor-role-missing";
    public const string PatientLinkMissing = "patient-link-missing";
    public const string PatientRoleMissing = "patient-role-missing";
    public const string MappingDisabled = "mapping-disabled";

    /// <summary>
    /// Steps that the enable operation completes itself.
    /// </summary>
    public static readonly IReadOnlySet<string> CompletedByEnable = new HashSet<string> { ProviderAccountDisabled, MappingDisabled };

    public static IReadOnlyList<string> PendingSteps(ApplicationIdentity identity, IdentityProviderAccount? account)
    {
        if (account is null)
            return [ProviderAccountMissing];

        var steps = new List<string>();
        if (!account.Enabled)
            steps.Add(ProviderAccountDisabled);
        if (identity.DoctorId is null && identity.PatientId is null)
            steps.Add(RecordLinkMissing);
        if (account.ApiRoles.Contains(ApiRoles.Doctor) && identity.DoctorId is null)
            steps.Add(DoctorLinkMissing);
        if (!account.ApiRoles.Contains(ApiRoles.Doctor) && identity.DoctorId is not null)
            steps.Add(DoctorRoleMissing);
        if (account.ApiRoles.Contains(ApiRoles.Patient) && identity.PatientId is null)
            steps.Add(PatientLinkMissing);
        if (!account.ApiRoles.Contains(ApiRoles.Patient) && identity.PatientId is not null)
            steps.Add(PatientRoleMissing);
        if (!identity.Enabled)
            steps.Add(MappingDisabled);
        return steps;
    }
}
