namespace MedicalApp.Features.Appointments.GetAppointment;

public record GetAppointmentRequest([property: RouteParam] Guid Id);
