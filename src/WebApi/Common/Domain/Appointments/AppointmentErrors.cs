namespace MedicalApp.Common.Domain.Appointments;

public static class AppointmentErrors
{
    public static readonly Error NotFound = Error.NotFound(
        "Appointment.NotFound",
        "Appointment is not found");

    public static readonly Error DoctorUnavailable = Error.Conflict(
        "Appointment.DoctorUnavailable", "The doctor already has an appointment during this time.");

    public static readonly Error InsufficientNotice = Error.Validation(
        "Appointment.InsufficientNotice", "Book appointments at least 15 minutes in advance.");

    public static readonly Error CannotCompleteCancelled = Error.Conflict(
        "Appointment.CannotCompleteCancelled", "Cannot complete a cancelled appointment.");

    public static readonly Error CannotCancelCompleted = Error.Conflict(
        "Appointment.CannotCancelCompleted", "Cannot cancel a completed appointment.");

    public static readonly Error ConcurrentUpdate = Error.Conflict(
        "Appointment.ConcurrentUpdate", "The appointment changed during this request. Reload it and try again.");
}
