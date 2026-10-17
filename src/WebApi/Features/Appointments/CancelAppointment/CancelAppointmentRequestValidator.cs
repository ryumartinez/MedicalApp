using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.CancelAppointment;

public class CancelAppointmentRequestValidator : Validator<CancelAppointmentRequest>
{
    public CancelAppointmentRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
        RuleFor(r => r.Reason).NotEmpty().MaximumLength(Appointment.CancellationReasonMaxLength);
    }
}
