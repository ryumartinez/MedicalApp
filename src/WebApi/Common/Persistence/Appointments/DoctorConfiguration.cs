using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Domain.Doctors;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.Appointments;

public class DoctorConfiguration : AuditableConfiguration<Doctor>
{
    public override void PostConfigure(EntityTypeBuilder<Doctor> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.FullName).HasMaxLength(Doctor.FullNameMaxLength).IsRequired();
        builder.Property(d => d.Specialty).HasMaxLength(Doctor.SpecialtyMaxLength).IsRequired();
    }
}
