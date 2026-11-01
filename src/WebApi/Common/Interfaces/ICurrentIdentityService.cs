using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Common.Interfaces;

/// <summary>
/// The caller's validated token identity and its current application mapping.
/// </summary>
/// <param name="DoctorId">Set only when the identity is enabled, linked to a doctor, and the token has the doctor role.</param>
/// <param name="PatientId">Set only when the identity is enabled, linked to a patient, and the token has the patient role.</param>
public sealed record CurrentIdentity(
    string Issuer,
    string Subject,
    IReadOnlySet<string> Roles,
    ApplicationIdentityId? ApplicationIdentityId,
    DoctorId? DoctorId,
    PatientId? PatientId)
{
    public bool IsAdministrator => Roles.Contains(Authentication.ApiRoles.Administrator);
}

public interface ICurrentIdentityService
{
    /// <summary>
    /// Resolves the caller against current application state. Returns null for unauthenticated callers.
    /// </summary>
    Task<CurrentIdentity?> GetAsync(CancellationToken cancellationToken = default);
}
