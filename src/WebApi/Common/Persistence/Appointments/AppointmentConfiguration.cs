using MedicalApp.Common.Domain.Appointments;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.Appointments;

public class AppointmentConfiguration : AuditableConfiguration<Appointment>
{
    public override void PostConfigure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Notes).HasMaxLength(Appointment.NotesMaxLength);
        builder.Property(a => a.CancellationReason).HasMaxLength(Appointment.CancellationReasonMaxLength);
        // Npgsql maps this uint concurrency token to PostgreSQL's xmin system column.
        builder.Property(a => a.RowVersion).IsRowVersion();

        builder.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Doctor).WithMany().HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.DoctorId, a.StartUtc, a.EndUtc });
    }
}
