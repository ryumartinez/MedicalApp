using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.CompleteAppointment;

public class CompleteAppointmentRequestValidator : Validator<CompleteAppointmentRequest>
{
    public CompleteAppointmentRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Notes).MaximumLength(Appointment.NotesMaxLength);
    }
}
