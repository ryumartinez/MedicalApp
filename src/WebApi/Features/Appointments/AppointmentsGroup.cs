namespace MedicalApp.Features.Appointments;

public class AppointmentsGroup : Group
{
    public AppointmentsGroup()
    {
        Configure("appointments", ep => ep.Description(x => x.ProducesProblemDetails(500)));
    }
}
