namespace MedicalApp.Features.Appointments;

public class AppointmentsGroup : Group
{
    public AppointmentsGroup()
    {
        Configure("appointments", ep =>
        {
            ep.AllowAnonymous();
            ep.Description(x => x.ProducesProblemDetails(500));
        });
    }
}
