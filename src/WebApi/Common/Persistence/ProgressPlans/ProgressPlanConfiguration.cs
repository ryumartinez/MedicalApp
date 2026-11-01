using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.ProgressPlans;

public class ProgressPlanConfiguration : AuditableConfiguration<ProgressPlan>
{
    public override void PostConfigure(EntityTypeBuilder<ProgressPlan> builder)
    {
        builder.HasKey(plan => plan.Id);
        builder.Property(plan => plan.TimeZoneId).HasMaxLength(ProgressSchedule.TimeZoneIdMaxLength).IsRequired();
        builder.Property(plan => plan.Revision).IsXminRevision();
        builder.Ignore(plan => plan.Schedule);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(plan => plan.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Patient>().WithMany().HasForeignKey(plan => plan.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressForm>().WithMany().HasForeignKey(plan => plan.FormId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressFormVersion>().WithMany().HasForeignKey(plan => plan.FormVersionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressPlan>().WithMany().HasForeignKey(plan => plan.ReplacedByPlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(plan => new { plan.PatientId, plan.DoctorId });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_ProgressPlan_EndDate", "\"EndDate\" IS NULL OR \"EndDate\" >= \"StartDate\"");
            table.HasCheckConstraint("CK_ProgressPlan_EndReason", "(\"EndedAt\" IS NULL) = (\"EndReason\" IS NULL)");
        });
    }
}

public class ProgressPlanGrantConfiguration : AuditableConfiguration<ProgressPlanGrant>
{
    public override void PostConfigure(EntityTypeBuilder<ProgressPlanGrant> builder)
    {
        builder.HasKey(grant => grant.Id);
        builder.HasOne<ProgressPlan>().WithMany().HasForeignKey(grant => grant.PlanId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(grant => grant.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(grant => new { grant.PlanId, grant.DoctorId }).IsUnique();
        builder.HasIndex(grant => grant.DoctorId);
    }
}
