namespace MedicalApp.Features.Appointments.GetAppointments;

public class GetAppointmentsRequestValidator : Validator<GetAppointmentsRequest>
{
    public GetAppointmentsRequestValidator()
    {
        RuleFor(r => r.PatientId).NotEqual(Guid.Empty).When(r => r.PatientId.HasValue);
        RuleFor(r => r.DoctorId).NotEqual(Guid.Empty).When(r => r.DoctorId.HasValue);
        RuleFor(r => r.Status).IsInEnum().When(r => r.Status.HasValue);
        RuleFor(r => r.ToUtc).GreaterThan(r => r.FromUtc)
            .When(r => r.FromUtc.HasValue && r.ToUtc.HasValue);
        RuleFor(r => r.Page).GreaterThan(0)
            .Must((request, page) => ((long)page - 1) * request.PageSize <= int.MaxValue)
            .WithMessage("The requested page exceeds the supported range.");
        RuleFor(r => r.PageSize).InclusiveBetween(1, 100);
    }
}
