namespace MedicalApp.Features.Appointments.GetAppointment;

public class GetAppointmentSummary : Summary<GetAppointmentEndpoint>
{
    public GetAppointmentSummary()
    {
        Summary = "Get an appointment";
        Description = "Returns appointment times, status, notes, and patient and doctor details.";
        Response(404, "Appointment not found.");
    }
}
