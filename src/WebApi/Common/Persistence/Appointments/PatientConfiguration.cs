using MedicalApp.Common.Domain.Appointments;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.Appointments;

public class PatientConfiguration : AuditableConfiguration<Patient>
{
    public override void PostConfigure(EntityTypeBuilder<Patient> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.FullName).HasMaxLength(Patient.FullNameMaxLength).IsRequired();
        builder.Property(p => p.Email).HasMaxLength(Patient.EmailMaxLength).IsRequired();
        builder.Property(p => p.Phone).HasMaxLength(Patient.PhoneMaxLength).IsRequired();
    }
}
