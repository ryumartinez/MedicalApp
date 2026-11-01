using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Common.Interfaces;

public static class CurrentIdentityExtensions
{
    /// <summary>
    /// The caller's doctor record. Endpoints behind the doctor policy always have one.
    /// </summary>
    public static async Task<DoctorId> GetDoctorIdAsync(this ICurrentIdentityService identities, CancellationToken cancellationToken) =>
        (await identities.GetAsync(cancellationToken))?.DoctorId
        ?? throw new InvalidOperationException("The doctor policy must protect this endpoint.");

    /// <summary>
    /// The caller's patient record. Endpoints behind the patient policy always have one.
    /// </summary>
    public static async Task<PatientId> GetPatientIdAsync(this ICurrentIdentityService identities, CancellationToken cancellationToken) =>
        (await identities.GetAsync(cancellationToken))?.PatientId
        ?? throw new InvalidOperationException("The patient policy must protect this endpoint.");
}
