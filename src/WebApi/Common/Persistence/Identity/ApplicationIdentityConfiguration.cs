using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.Identity;

public class ApplicationIdentityConfiguration : AuditableConfiguration<ApplicationIdentity>
{
    public override void PostConfigure(EntityTypeBuilder<ApplicationIdentity> builder)
    {
        builder.HasKey(identity => identity.Id);
        builder.Property(identity => identity.Issuer).IsRequired();
        builder.Property(identity => identity.Subject).IsRequired();
        builder.HasIndex(identity => new { identity.Issuer, identity.Subject }).IsUnique();
        builder.HasIndex(identity => identity.DoctorId).IsUnique();
        builder.HasIndex(identity => identity.PatientId).IsUnique();
        builder.HasOne<Doctor>().WithMany().HasForeignKey(identity => identity.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Patient>().WithMany().HasForeignKey(identity => identity.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.ToTable(table => table.HasCheckConstraint("CK_ApplicationIdentity_EnabledMapping", "NOT \"Enabled\" OR \"DoctorId\" IS NOT NULL OR \"PatientId\" IS NOT NULL"));
    }
}
