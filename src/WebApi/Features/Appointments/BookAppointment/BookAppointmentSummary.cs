namespace MedicalApp.Features.Appointments.BookAppointment;

public class BookAppointmentSummary : Summary<BookAppointmentEndpoint>
{
    public BookAppointmentSummary()
    {
        Summary = "Book an appointment";
        Description = "Use UTC times and book at least 15 minutes ahead. Duration must be 10 minutes to 8 hours. " +
            "Appointments for one doctor cannot overlap. Adjacent appointments are allowed. " +
            "Cancelled appointments do not block new bookings.";
        Response(201, "Appointment booked. The Location header links to the appointment.");
        Response(400, "Invalid booking details.");
        Response(404, "Patient or doctor not found.");
        Response(409, "The doctor already has an appointment during this time.");
    }
}
