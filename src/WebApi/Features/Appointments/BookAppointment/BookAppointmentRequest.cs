namespace MedicalApp.Features.Appointments.BookAppointment;

public record BookAppointmentRequest(Guid PatientId, Guid DoctorId, DateTime StartUtc, DateTime EndUtc,
    string? Notes = null);
