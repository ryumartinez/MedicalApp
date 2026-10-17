namespace MedicalApp.Features.Appointments.CompleteAppointment;

public class CompleteAppointmentSummary : Summary<CompleteAppointmentEndpoint>
{
    public CompleteAppointmentSummary()
    {
        Summary = "Complete an appointment";
        Description = "Completes a scheduled appointment. Cancelled appointments cannot be completed. Repeated calls return success without changing the original result.";
        Response(204, "Appointment is completed.");
        Response(400, "Invalid request.");
        Response(404, "Appointment not found.");
        Response(409, "The appointment state does not allow this change.");
    }
}
