using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.Identity;

public class DoctorPatientRelationshipConfiguration : AuditableConfiguration<DoctorPatientRelationship>
{
    public override void PostConfigure(EntityTypeBuilder<DoctorPatientRelationship> builder)
    {
        builder.HasKey(relationship => relationship.Id);
        builder.HasOne<Doctor>().WithMany().HasForeignKey(relationship => relationship.DoctorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Patient>().WithMany().HasForeignKey(relationship => relationship.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(relationship => new { relationship.DoctorId, relationship.PatientId }).IsUnique();
    }
}
