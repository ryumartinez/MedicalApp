namespace MedicalApp.Features.Appointments.GetAppointment;

public class GetAppointmentRequestValidator : Validator<GetAppointmentRequest>
{
    public GetAppointmentRequestValidator()
    {
        RuleFor(r => r.Id).NotEmpty();
    }
}
