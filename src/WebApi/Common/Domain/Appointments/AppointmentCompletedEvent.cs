namespace MedicalApp.Common.Domain.Appointments;

public record AppointmentCompletedEvent(
    AppointmentId AppointmentId,
    PatientId PatientId,
    DoctorId DoctorId,
    DateTime CompletedUtc,
    string? Notes) : IEvent;
