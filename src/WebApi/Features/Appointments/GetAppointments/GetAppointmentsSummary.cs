namespace MedicalApp.Features.Appointments.GetAppointments;

public class GetAppointmentsSummary : Summary<GetAppointmentsEndpoint>
{
    public GetAppointmentsSummary()
    {
        Summary = "Get appointments";
        Description = "Filters combine patient, doctor, status, and UTC start times. " +
            "Use ISO 8601 timestamps with Z or an explicit offset. FromUtc is inclusive; ToUtc is exclusive. " +
            "Results sort by start time, then ID. " +
            "Page starts at 1. PageSize defaults to 20 and must be between 1 and 100. " +
            "TotalCount counts all matching appointments before pagination.";
        Response(400, "Invalid filters or pagination.");
    }
}
