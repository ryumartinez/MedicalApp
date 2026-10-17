using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.BookAppointment;

public class BookAppointmentRequestValidator : Validator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(r => r.PatientId).NotEmpty();
        RuleFor(r => r.DoctorId).NotEmpty();
        RuleFor(r => r.StartUtc).Must(value => value.Kind == DateTimeKind.Utc)
            .WithMessage("Start time must be in UTC.");
        RuleFor(r => r.EndUtc).Must(value => value.Kind == DateTimeKind.Utc)
            .WithMessage("End time must be in UTC.");
        RuleFor(r => r.StartUtc)
            .Must(value => value - timeProvider.GetUtcNow().UtcDateTime >=
                TimeSpan.FromMinutes(Appointment.MinimumBookingAdvanceMinutes))
            .WithMessage("Book appointments at least 15 minutes in advance.");
        RuleFor(r => r.EndUtc).Must((request, end) =>
                end - request.StartUtc >= TimeSpan.FromMinutes(Appointment.MinimumAppointmentDurationMinutes) &&
                end - request.StartUtc <= TimeSpan.FromHours(Appointment.MaximumAppointmentDurationHours))
            .WithMessage("Appointment duration must be between 10 minutes and 8 hours.");
        RuleFor(r => r.Notes).MaximumLength(Appointment.NotesMaxLength);
    }
}
