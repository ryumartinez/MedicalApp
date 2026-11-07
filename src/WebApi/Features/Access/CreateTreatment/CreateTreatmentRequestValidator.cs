namespace MedicalApp.Features.Access.CreateTreatment;

public class CreateTreatmentRequestValidator : Validator<CreateTreatmentRequest>
{
    public CreateTreatmentRequestValidator()
    {
        RuleFor(v => v.DoctorId).NotEmpty();
        RuleFor(v => v.PatientId).NotEmpty();
    }
}
