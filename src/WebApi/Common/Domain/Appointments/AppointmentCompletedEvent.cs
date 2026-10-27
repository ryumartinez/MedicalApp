namespace MedicalApp.Common.Domain.Appointments;

public record AppointmentCompletedEvent(
    AppointmentId AppointmentId,
    Patients.PatientId PatientId,
    Doctors.DoctorId DoctorId,
    DateTime CompletedUtc,
    string? Notes) : IEvent;
