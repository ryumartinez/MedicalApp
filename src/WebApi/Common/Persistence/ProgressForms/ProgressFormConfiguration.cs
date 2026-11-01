using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.ProgressForms;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.ProgressForms;

public class ProgressFormConfiguration : AuditableConfiguration<ProgressForm>
{
    public override void PostConfigure(EntityTypeBuilder<ProgressForm> builder)
    {
        builder.HasKey(form => form.Id);
        builder.Property(form => form.Title).HasMaxLength(ProgressForm.TitleMaxLength).IsRequired();
        builder.Property(form => form.Description).HasMaxLength(ProgressForm.DescriptionMaxLength);
        builder.Property(form => form.DraftQuestions).HasJsonConversion().IsRequired();
        builder.Property(form => form.Revision).IsXminRevision();
        builder.HasOne<Doctor>().WithMany().HasForeignKey(form => form.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressForm>().WithMany().HasForeignKey(form => form.SourceFormId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(form => form.DoctorId);
    }
}

public class ProgressFormVersionConfiguration : AuditableConfiguration<ProgressFormVersion>
{
    public override void PostConfigure(EntityTypeBuilder<ProgressFormVersion> builder)
    {
        builder.HasKey(version => version.Id);
        builder.Property(version => version.Title).HasMaxLength(ProgressForm.TitleMaxLength).IsRequired();
        builder.Property(version => version.Description).HasMaxLength(ProgressForm.DescriptionMaxLength);
        builder.Property(version => version.Questions).HasJsonConversion().IsRequired();
        builder.HasOne<ProgressForm>().WithMany().HasForeignKey(version => version.FormId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(version => version.DoctorId).OnDelete(DeleteBehavior.Restrict);
        // Concurrent publications cannot both take the same number.
        builder.HasIndex(version => new { version.FormId, version.VersionNumber }).IsUnique();
        builder.ToTable(table => table.HasCheckConstraint("CK_ProgressFormVersion_VersionNumber", "\"VersionNumber\" > 0"));
    }
}
