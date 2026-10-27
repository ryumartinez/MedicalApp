using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;
using DoctorId = MedicalApp.Common.Domain.Doctors.DoctorId;
using PatientId = MedicalApp.Common.Domain.Patients.PatientId;

namespace MedicalApp.Features.Appointments.GetAppointments;

public class GetAppointmentsEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetAppointmentsRequest, GetAppointmentsResponse>
{
    public override void Configure()
    {
        Get("/");
        Group<AppointmentsGroup>();
        Description(x => x.WithName("GetAppointments").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetAppointmentsRequest req, CancellationToken ct)
    {
        var patientId = req.PatientId.HasValue ? PatientId.From(req.PatientId.Value) : (PatientId?)null;
        var doctorId = req.DoctorId.HasValue ? DoctorId.From(req.DoctorId.Value) : (DoctorId?)null;
        var fromUtc = req.FromUtc?.UtcDateTime;
        var toUtc = req.ToUtc?.UtcDateTime;
        var spec = AppointmentSpec.Filtered(patientId, doctorId, req.Status, fromUtc, toUtc);
        var totalCount = await dbContext.Appointments.WithSpecification(spec).CountAsync(ct);
        var appointments = await dbContext.Appointments.AsNoTracking()
            .WithSpecification(AppointmentSpec.Page(patientId, doctorId, req.Status, fromUtc, toUtc,
                req.Page, req.PageSize))
            .Select(a => new GetAppointmentsResponse.AppointmentDto(a.Id.Value, a.PatientId.Value,
                a.DoctorId.Value, a.StartUtc, a.EndUtc, a.Status, a.Notes))
            .ToListAsync(ct);

        await Send.OkAsync(new GetAppointmentsResponse(appointments, totalCount, req.Page, req.PageSize), ct);
    }
}
