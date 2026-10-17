namespace MedicalApp.Features.Appointments.CancelAppointment;

public class CancelAppointmentSummary : Summary<CancelAppointmentEndpoint>
{
    public CancelAppointmentSummary()
    {
        Summary = "Cancel an appointment";
        Description = "Cancels a scheduled appointment. Completed appointments cannot be cancelled. A reason is required. Repeated calls return success without changing the original result.";
        Response(204, "Appointment is cancelled.");
        Response(400, "Invalid request.");
        Response(404, "Appointment not found.");
        Response(409, "The appointment state does not allow this change.");
    }
}
