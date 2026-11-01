using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MigrationService.Initializers;

/// <summary>
/// Seeds development doctor and patient records, their enabled identity mappings, and a treatment relationship.
/// Subjects match the development accounts in tools/AppHost/Keycloak/Realms/medicalapp-realm.json.
/// Existing rows are left unchanged, so repeated runs create nothing new.
/// </summary>
public class ProgressIdentityInitializer(ApplicationDbContext dbContext, IConfiguration configuration, ILogger<ProgressIdentityInitializer> logger)
{
    public const string IssuerKey = "Authentication:Keycloak:DevelopmentIssuer";

    public const string DoctorSubject = "01967a92-2ff8-7a00-8000-000000000001";
    public const string PatientSubject = "01967a92-2ff8-7a00-8000-000000000002";

    public static readonly DoctorId DoctorId = DoctorId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000101"));
    public static readonly PatientId PatientId = PatientId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000201"));
    public static readonly ApplicationIdentityId DoctorIdentityId = ApplicationIdentityId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000301"));
    public static readonly ApplicationIdentityId PatientIdentityId = ApplicationIdentityId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000302"));
    public static readonly DoctorPatientRelationshipId TreatmentId = DoctorPatientRelationshipId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000401"));

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var issuer = configuration[IssuerKey];
        if (string.IsNullOrWhiteSpace(issuer))
        {
            logger.LogWarning("Skipping development identity seed data because {IssuerKey} is not configured.", IssuerKey);
            return;
        }

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
