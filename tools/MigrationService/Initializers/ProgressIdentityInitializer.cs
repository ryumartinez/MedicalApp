using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MigrationService.Initializers;

/// <summary>
/// Seeds development doctor and patient records, their enabled identity mappings, and a treatment relationship.
/// Subjects match the development accounts that <see cref="DevelopmentAccountInitializer"/> creates.
/// Existing rows are left unchanged, so repeated runs create nothing new. The only exception: seeded mappings
/// from an earlier issuer, such as the former Keycloak realm, move to the current issuer.
/// </summary>
public class ProgressIdentityInitializer(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<ProgressIdentityInitializer> logger)
{
    public const string IssuerKey = "Authentication:DevelopmentIssuer";

    public const string DoctorSubject = "01967a92-2ff8-7a00-8000-000000000001";
    public const string PatientSubject = "01967a92-2ff8-7a00-8000-000000000002";

    public static readonly DoctorId DoctorId = DoctorId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000101"));
    public static readonly PatientId PatientId = PatientId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000201"));
    public static readonly ApplicationIdentityId DoctorIdentityId = ApplicationIdentityId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000301"));
    public static readonly ApplicationIdentityId PatientIdentityId = ApplicationIdentityId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000302"));
    public static readonly DoctorPatientRelationshipId TreatmentId = DoctorPatientRelationshipId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000401"));

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var configured = configuration[IssuerKey];
        if (string.IsNullOrWhiteSpace(configured))
        {
            logger.LogWarning("Skipping development identity seed data because {IssuerKey} is not configured.", IssuerKey);
            return;
        }

        // Same normalization as the authorization server, so stored issuers equal the token's iss claim.
        var issuer = IdentityIssuer.Normalize(configured, requireHttps: false).AbsoluteUri;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            if (!await dbContext.Doctors.AnyAsync(doctor => doctor.Id == DoctorId, cancellationToken))
            {
                var doctor = Doctor.Create("Development Doctor", "Psychology");
                doctor.Id = DoctorId;
                dbContext.Doctors.Add(doctor);
            }

            if (!await dbContext.Patients.AnyAsync(patient => patient.Id == PatientId, cancellationToken))
            {
                var patient = Patient.Create("Development Patient", "patient@example.invalid", "555-010-0100");
                patient.Id = PatientId;
                dbContext.Patients.Add(patient);
            }

            await MoveToIssuerAsync(DoctorIdentityId, issuer, DoctorSubject, cancellationToken);
            await MoveToIssuerAsync(PatientIdentityId, issuer, PatientSubject, cancellationToken);
            await AddIdentityAsync(DoctorIdentityId, issuer, DoctorSubject, identity => identity.LinkDoctor(DoctorId), cancellationToken);
            await AddIdentityAsync(PatientIdentityId, issuer, PatientSubject, identity => identity.LinkPatient(PatientId), cancellationToken);

            if (!await dbContext.DoctorPatientRelationships.AnyAsync(
                    relationship => relationship.DoctorId == DoctorId && relationship.PatientId == PatientId, cancellationToken))
            {
                var treatment = DoctorPatientRelationship.Create(DoctorId, PatientId);
                treatment.Id = TreatmentId;
                dbContext.DoctorPatientRelationships.Add(treatment);
            }

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    /// <summary>
    /// Rewrites only the issuer of a seeded mapping. Its links and enabled state stay as they are.
    /// </summary>
    private async Task MoveToIssuerAsync(ApplicationIdentityId id, string issuer, string subject, CancellationToken cancellationToken)
    {
        // Another mapping already owns the pair. Leave both alone rather than violate the unique index.
        if (await dbContext.ApplicationIdentities.AnyAsync(identity => identity.Id != id && identity.Issuer == issuer && identity.Subject == subject, cancellationToken))
            return;

        var moved = await dbContext.ApplicationIdentities
            .Where(identity => identity.Id == id && identity.Subject == subject && identity.Issuer != issuer)
            .ExecuteUpdateAsync(update => update.SetProperty(identity => identity.Issuer, issuer), cancellationToken);
        if (moved != 0)
            logger.MovedMapping(id.Value, issuer);
    }

    private async Task AddIdentityAsync(ApplicationIdentityId id, string issuer, string subject, Action<ApplicationIdentity> link,
        CancellationToken cancellationToken)
    {
        // An identity that already exists keeps its state, including a deliberate disablement.
        if (await dbContext.ApplicationIdentities.AnyAsync(identity => identity.Id == id || (identity.Issuer == issuer && identity.Subject == subject), cancellationToken))
            return;

        var identity = ApplicationIdentity.Create(id, issuer, subject);
        link(identity);
        identity.Enable();
        dbContext.ApplicationIdentities.Add(identity);
    }
}

internal static partial class ProgressIdentityInitializerLog
{
    [LoggerMessage(LogLevel.Information, "Moved development mapping {MappingId} to issuer {Issuer}.")]
    public static partial void MovedMapping(this ILogger logger, Guid mappingId, string issuer);
}
