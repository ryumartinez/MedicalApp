using MedicalApp.Common.Domain.Base;

namespace MedicalApp.Common.Domain.Appointments;

[ValueObject<Guid>]
public readonly partial struct AppointmentId;

public class Appointment : AggregateRoot<AppointmentId>
{
    public const int NotesMaxLength = 1024;
    public const int CancellationReasonMaxLength = 512;
    public const int MinimumAppointmentDurationMinutes = 10;
    public const int MaximumAppointmentDurationHours = 8;
    public const int MinimumBookingAdvanceMinutes = 15;

    public PatientId PatientId { get; private set; }
    public DoctorId DoctorId { get; private set; }

    public DateTime StartUtc
    {
        get;
        private set
        {
            ValidateUtc(value, nameof(StartUtc));
            field = value;
        }
    }

    public DateTime EndUtc
    {
        get;
        private set
        {
            ValidateUtc(value, nameof(EndUtc));
            var duration = value - StartUtc;
            ThrowIfLessThan(duration, TimeSpan.FromMinutes(MinimumAppointmentDurationMinutes), nameof(EndUtc));
            ThrowIfGreaterThan(duration, TimeSpan.FromHours(MaximumAppointmentDurationHours), nameof(EndUtc));
            field = value;
        }
    }

    public AppointmentStatus Status { get; private set; }

    public string? Notes
    {
        get;
        private set
        {
            if (value is not null)
                ThrowIfGreaterThan(value.Length, NotesMaxLength, nameof(Notes));

            field = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
    }

    public DateTime? CompletedUtc
    {
        get;
        private set
        {
            if (value.HasValue)
                ValidateUtc(value.Value, nameof(CompletedUtc));

            field = value;
        }
    }

    public DateTime? CancelledUtc
    {
        get;
        private set
        {
            if (value.HasValue)
                ValidateUtc(value.Value, nameof(CancelledUtc));

            field = value;
        }
    }

    public string? CancellationReason
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(CancellationReason));
            ThrowIfGreaterThan(value.Length, CancellationReasonMaxLength, nameof(CancellationReason));
            field = value;
        }
    }

    public byte[] RowVersion { get; private set; } = [];

    public Patient Patient { get; private set; } = null!;
    public Doctor Doctor { get; private set; } = null!;

    private Appointment()
    {
    }

    public static Appointment Create(PatientId patientId, DoctorId doctorId, DateTime startUtc,
        DateTime endUtc, string? notes = null)
    {
        var appointment = new Appointment
        {
            Id = AppointmentId.From(Guid.CreateVersion7()),
            PatientId = patientId,
            DoctorId = doctorId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Status = AppointmentStatus.Scheduled,
            Notes = notes
        };

        appointment.AddDomainEvent(new AppointmentBookedEvent(
            appointment.Id, patientId, doctorId, startUtc, endUtc));

        return appointment;
    }

    public void Complete(string? notes = null, DateTime? completedAtUtc = null)
    {
        if (Status == AppointmentStatus.Cancelled)
            throw new InvalidOperationException("Cannot complete a cancelled appointment");

        if (Status == AppointmentStatus.Completed)
            return;

        var timestamp = completedAtUtc ?? DateTime.UtcNow;
        ValidateUtc(timestamp, nameof(completedAtUtc));
        Notes = notes;
        CompletedUtc = timestamp;
        Status = AppointmentStatus.Completed;

        AddDomainEvent(new AppointmentCompletedEvent(Id, PatientId, DoctorId, timestamp, Notes));
    }

    public void Cancel(string reason, DateTime? cancelledAtUtc = null)
    {
        ThrowIfNullOrWhiteSpace(reason, nameof(reason));
        ThrowIfGreaterThan(reason.Length, CancellationReasonMaxLength, nameof(reason));

        if (Status == AppointmentStatus.Completed)
            throw new InvalidOperationException("Cannot cancel a completed appointment");

        if (Status == AppointmentStatus.Cancelled)
            return;

        var timestamp = cancelledAtUtc ?? DateTime.UtcNow;
        ValidateUtc(timestamp, nameof(cancelledAtUtc));
        CancellationReason = reason;
        CancelledUtc = timestamp;
        Status = AppointmentStatus.Cancelled;
    }

    public void UpdateNotes(string? notes) => Notes = notes;

    private static void ValidateUtc(DateTime value, string parameterName)
    {
        if (value.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Timestamp must be in UTC", parameterName);
    }
}
