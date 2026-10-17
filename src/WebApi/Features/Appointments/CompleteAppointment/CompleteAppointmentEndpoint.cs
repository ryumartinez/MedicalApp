using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.CompleteAppointment;

public class CompleteAppointmentEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : Endpoint<CompleteAppointmentRequest>
{
    public override void Configure()
    {
        Post("/{id}/complete");
        Group<AppointmentsGroup>();
        Description(x => x.WithName("CompleteAppointment")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CompleteAppointmentRequest req, CancellationToken ct)
    {
        var id = AppointmentId.From(req.Id);
        var appointment = await dbContext.Appointments
            .WithSpecification(AppointmentSpec.ById(id)).SingleOrDefaultAsync(ct);
        if (appointment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            var error = AppointmentErrors.CannotCompleteCancelled;
            AddError(error.Description, error.Code);
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        if (appointment.Status == AppointmentStatus.Completed)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        appointment.Complete(req.Notes, timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.Appointments.AsNoTracking()
                .WithSpecification(AppointmentSpec.ById(id)).SingleOrDefaultAsync(ct);
            if (current?.Status != AppointmentStatus.Completed)
            {
                var error = AppointmentErrors.ConcurrentUpdate;
                AddError(error.Description, error.Code);
                await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
                return;
            }
        }

        await Send.NoContentAsync(ct);
    }
}
