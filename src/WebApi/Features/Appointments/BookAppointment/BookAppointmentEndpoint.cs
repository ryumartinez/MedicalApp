using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using DoctorId = MedicalApp.Common.Domain.Doctors.DoctorId;
using PatientId = MedicalApp.Common.Domain.Patients.PatientId;

namespace MedicalApp.Features.Appointments.BookAppointment;

public class BookAppointmentEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : Endpoint<BookAppointmentRequest, BookAppointmentResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<AppointmentsGroup>();
        Description(x => x.WithName("BookAppointment")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(BookAppointmentRequest req, CancellationToken ct)
    {
        var result = await dbContext.Database.CreateExecutionStrategy()
            .ExecuteAsync(() => BookAsync(req, ct));

        if (result.IsError)
        {
            result.Errors.ForEach(error => AddError(error.Description, error.Code));
            var statusCode = result.FirstError.Type switch
            {
                ErrorType.NotFound => StatusCodes.Status404NotFound,
                ErrorType.Conflict => StatusCodes.Status409Conflict,
                _ => StatusCodes.Status400BadRequest
            };
            await Send.ErrorsAsync(statusCode, ct);
            return;
        }

        await Send.CreatedAtAsync("GetAppointment", new { id = result.Value.Id.Value },
            new BookAppointmentResponse(result.Value.Id.Value), cancellation: ct);
    }

    private async Task<ErrorOr<Appointment>> BookAsync(BookAppointmentRequest req, CancellationToken ct)
    {
        // A retry must load fresh state after a rolled-back attempt.
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);
        var doctorId = DoctorId.From(req.DoctorId);
        var patientId = PatientId.From(req.PatientId);

        // Hold one update lock per doctor until commit. Concurrent bookings for that doctor wait here.
        var doctor = await dbContext.Doctors
            .FromSqlInterpolated($"SELECT * FROM [Doctors] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {req.DoctorId}")
            .WithSpecification(DoctorSpec.ById(doctorId))
            .SingleOrDefaultAsync(ct);
        if (doctor is null)
            return DoctorErrors.NotFound;

        if (!await dbContext.Patients.WithSpecification(PatientSpec.ById(patientId)).AnyAsync(ct))
            return PatientErrors.NotFound;

        if (await dbContext.Appointments
                .WithSpecification(AppointmentSpec.Overlapping(doctorId, req.StartUtc, req.EndUtc)).AnyAsync(ct))
            return AppointmentErrors.DoctorUnavailable;

        // Recheck after waiting for the lock so the booking still meets the advance notice rule.
        if (req.StartUtc - timeProvider.GetUtcNow().UtcDateTime <
            TimeSpan.FromMinutes(Appointment.MinimumBookingAdvanceMinutes))
            return AppointmentErrors.InsufficientNotice;

        var appointment = Appointment.Create(patientId, doctorId, req.StartUtc, req.EndUtc, req.Notes);
        dbContext.Appointments.Add(appointment);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return appointment;
    }
}
