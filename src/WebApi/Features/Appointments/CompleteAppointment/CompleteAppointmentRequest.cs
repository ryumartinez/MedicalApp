namespace MedicalApp.Features.Appointments.CompleteAppointment;

public record CompleteAppointmentRequest([property: RouteParam] Guid Id, string? Notes = null);
