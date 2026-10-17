namespace MedicalApp.Common.Domain.Appointments;

public record AppointmentBookedEvent(
    AppointmentId AppointmentId,
    PatientId PatientId,
    DoctorId DoctorId,
    DateTime StartUtc,
    DateTime EndUtc) : IEvent;
