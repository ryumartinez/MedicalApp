using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Common.Persistence;

/// <summary>
/// The shared coordination protocol for treatment revocation, plan closure, draft changes, submission, and media cleanup.
/// Each operation takes row locks inside its transaction in this order and never in reverse:
/// treatment relationship, then plans by ID, then the entry, then media assets by ID.
/// </summary>
/// <remarks>
/// Closure and submission both lock the plan row, so whichever commits first decides the outcome for the other.
/// Cleanup locks only media assets and skips rows that a submission holds.
/// Network calls to storage never run while these locks are held.
/// Queries select <c>xmin</c> explicitly because <c>SELECT *</c> omits system columns.
/// </remarks>
public static class ProgressLocks
{
    public static Task<DoctorPatientRelationship?> LockTreatmentAsync(this ApplicationDbContext db, DoctorPatientRelationshipId id,
        CancellationToken cancellationToken) =>
        db.DoctorPatientRelationships
            .FromSqlInterpolated($"SELECT * FROM \"DoctorPatientRelationships\" WHERE \"Id\" = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<ProgressPlan?> LockPlanAsync(this ApplicationDbContext db, ProgressPlanId id, CancellationToken cancellationToken) =>
        db.ProgressPlans
            .FromSqlInterpolated($"SELECT *, xmin FROM \"ProgressPlans\" WHERE \"Id\" = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>
    /// Locks a doctor's open plans for one patient in ID order.
    /// </summary>
    public static Task<List<ProgressPlan>> LockOpenPlansAsync(this ApplicationDbContext db, DoctorId doctorId, PatientId patientId,
        CancellationToken cancellationToken) =>
        db.ProgressPlans
            .FromSqlInterpolated($"""
                SELECT *, xmin FROM "ProgressPlans"
                WHERE "DoctorId" = {doctorId.Value} AND "PatientId" = {patientId.Value} AND "EndedAt" IS NULL
                ORDER BY "Id" FOR UPDATE
                """)
            .ToListAsync(cancellationToken);

    public static Task<ProgressEntry?> LockEntryAsync(this ApplicationDbContext db, ProgressEntryId id, CancellationToken cancellationToken) =>
        db.ProgressEntries
            .FromSqlInterpolated($"SELECT *, xmin FROM \"ProgressEntries\" WHERE \"Id\" = {id.Value} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<ProgressEntry?> LockEntryAsync(this ApplicationDbContext db, ProgressPlanId planId, DateOnly periodStart,
        CancellationToken cancellationToken) =>
        db.ProgressEntries
            .FromSqlInterpolated($"SELECT *, xmin FROM \"ProgressEntries\" WHERE \"PlanId\" = {planId.Value} AND \"PeriodStartDate\" = {periodStart} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public static Task<List<MediaAsset>> LockAssetsAsync(this ApplicationDbContext db, IReadOnlyCollection<MediaAssetId> ids,
        CancellationToken cancellationToken)
    {
        var values = ids.Select(id => id.Value).Distinct().ToArray();
        return db.MediaAssets
            .FromSqlInterpolated($"SELECT *, xmin FROM \"MediaAssets\" WHERE \"Id\" = ANY({values}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
    }

    /// <summary>
    /// Claims cleanup candidates. Rows locked by a running submission are skipped and reconsidered on the next run.
    /// </summary>
    public static Task<List<MediaAsset>> ClaimExpirableAssetsAsync(this ApplicationDbContext db, DateTimeOffset pendingDeadline,
        DateTimeOffset readyDeadline, int limit, CancellationToken cancellationToken) =>
        db.MediaAssets
            .FromSqlInterpolated($"""
                SELECT *, xmin FROM "MediaAssets"
                WHERE ("State" = 0 AND "UploadExpiresAt" <= {pendingDeadline})
                   OR ("State" = 1 AND "ReadyAt" <= {readyDeadline})
                ORDER BY "Id" LIMIT {limit} FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(cancellationToken);
}
