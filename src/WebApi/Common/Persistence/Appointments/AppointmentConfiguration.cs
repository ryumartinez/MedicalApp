using MedicalApp.Common.Domain.Appointments;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace MedicalApp.Common.Persistence.Appointments;

public class AppointmentConfiguration : AuditableConfiguration<Appointment>
{
    public override void PostConfigure(EntityTypeBuilder<Appointment> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Notes).HasMaxLength(Appointment.NotesMaxLength);
        builder.Property(a => a.CancellationReason).HasMaxLength(Appointment.CancellationReasonMaxLength);
        builder.Property(a => a.RowVersion).IsRowVersion();

        // SQL Server datetime2 does not store DateTimeKind. Restore UTC on reads.
        var utcConverter = new ValueConverter<DateTime, DateTime>(
            value => value,
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Property(a => a.StartUtc).HasConversion(utcConverter);
        builder.Property(a => a.EndUtc).HasConversion(utcConverter);
        builder.Property(a => a.CompletedUtc).HasConversion(utcConverter);
        builder.Property(a => a.CancelledUtc).HasConversion(utcConverter);

        builder.HasOne(a => a.Patient).WithMany().HasForeignKey(a => a.PatientId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(a => a.Doctor).WithMany().HasForeignKey(a => a.DoctorId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => new { a.DoctorId, a.StartUtc, a.EndUtc });
    }
}
