using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.GetAppointment;

public class GetAppointmentEndpoint(ApplicationDbContext dbContext)
    : Endpoint<GetAppointmentRequest, GetAppointmentResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<AppointmentsGroup>();
        Description(x => x.WithName("GetAppointment").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(GetAppointmentRequest req, CancellationToken ct)
    {
        var appointment = await dbContext.Appointments.AsNoTracking()
            .WithSpecification(AppointmentSpec.ById(AppointmentId.From(req.Id))).SingleOrDefaultAsync(ct);
        if (appointment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new GetAppointmentResponse(
            appointment.Id.Value, appointment.PatientId.Value, appointment.DoctorId.Value,
            appointment.StartUtc, appointment.EndUtc, appointment.Status, appointment.Notes,
            appointment.CompletedUtc, appointment.CancelledUtc, appointment.CancellationReason,
            new GetAppointmentResponse.PatientDto(appointment.Patient.Id.Value, appointment.Patient.FullName,
                appointment.Patient.Email, appointment.Patient.Phone),
            new GetAppointmentResponse.DoctorDto(appointment.Doctor.Id.Value, appointment.Doctor.FullName,
                appointment.Doctor.Specialty)), ct);
    }
}
