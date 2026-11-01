using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.ProgressEntries;

public class ProgressEntryConfiguration : AuditableConfiguration<ProgressEntry>
{
    public override void PostConfigure(EntityTypeBuilder<ProgressEntry> builder)
    {
        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Answers).HasJsonConversion().IsRequired();
        builder.Property(entry => entry.Revision).IsXminRevision();
        builder.Ignore(entry => entry.Period);
        builder.HasOne<ProgressPlan>().WithMany().HasForeignKey(entry => entry.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Patient>().WithMany().HasForeignKey(entry => entry.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressFormVersion>().WithMany().HasForeignKey(entry => entry.FormVersionId).OnDelete(DeleteBehavior.Restrict);
        // One entry per plan period, enforced even under concurrent draft creation.
        builder.HasIndex(entry => new { entry.PlanId, entry.PeriodStartDate }).IsUnique();
        builder.HasIndex(entry => new { entry.PatientId, entry.PeriodStartDate });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ProgressEntry_Submitted",
                "(\"Status\" = 1) = (\"SubmittedAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_ProgressEntry_Period", "\"PeriodEndDate\" >= \"PeriodStartDate\" AND \"PeriodEndUtc\" > \"PeriodStartUtc\"");
        });
    }
}
