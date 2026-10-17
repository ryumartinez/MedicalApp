namespace MedicalApp.Features.Appointments.CancelAppointment;

public record CancelAppointmentRequest([property: RouteParam] Guid Id, string Reason);
