namespace MedicalApp.Common.Domain.Appointments;

public record AppointmentBookedEvent(
    AppointmentId AppointmentId,
    Patients.PatientId PatientId,
    Doctors.DoctorId DoctorId,
    DateTime StartUtc,
    DateTime EndUtc) : IEvent;
