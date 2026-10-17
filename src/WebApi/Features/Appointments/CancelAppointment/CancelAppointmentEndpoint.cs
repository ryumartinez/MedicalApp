using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.CancelAppointment;

public class CancelAppointmentEndpoint(ApplicationDbContext dbContext, TimeProvider timeProvider)
    : Endpoint<CancelAppointmentRequest>
{
    public override void Configure()
    {
        Post("/{id}/cancel");
        Group<AppointmentsGroup>();
        Description(x => x.WithName("CancelAppointment")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CancelAppointmentRequest req, CancellationToken ct)
    {
        var id = AppointmentId.From(req.Id);
        var appointment = await dbContext.Appointments
            .WithSpecification(AppointmentSpec.ById(id)).SingleOrDefaultAsync(ct);
        if (appointment is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (appointment.Status == AppointmentStatus.Completed)
        {
            var error = AppointmentErrors.CannotCancelCompleted;
            AddError(error.Description, error.Code);
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        if (appointment.Status == AppointmentStatus.Cancelled)
        {
            await Send.NoContentAsync(ct);
            return;
        }

        appointment.Cancel(req.Reason, timeProvider.GetUtcNow().UtcDateTime);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            dbContext.ChangeTracker.Clear();
            var current = await dbContext.Appointments.AsNoTracking()
                .WithSpecification(AppointmentSpec.ById(id)).SingleOrDefaultAsync(ct);
            if (current?.Status != AppointmentStatus.Cancelled)
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
