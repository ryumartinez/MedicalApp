using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Features.Accounts.LinkAccount;

public class LinkAccountRequestValidator : Validator<LinkAccountRequest>
{
    public LinkAccountRequestValidator()
    {
        RuleFor(v => v.Id).NotEmpty();

        RuleFor(v => v)
            .Must(v => v.DoctorId is not null || v.PatientId is not null || v.NewDoctor is not null || v.NewPatient is not null)
            .WithMessage("Specify a doctor or patient record to link.");
        RuleFor(v => v)
            .Must(v => v.DoctorId is null || v.NewDoctor is null)
            .WithMessage("Specify either an existing doctor or a new doctor.");
        RuleFor(v => v)
            .Must(v => v.PatientId is null || v.NewPatient is null)
            .WithMessage("Specify either an existing patient or a new patient.");

        RuleFor(v => v.DoctorId).NotEqual(Guid.Empty).When(v => v.DoctorId is not null);
        RuleFor(v => v.PatientId).NotEqual(Guid.Empty).When(v => v.PatientId is not null);

        RuleFor(v => v.NewDoctor!).ChildRules(doctor =>
        {
            doctor.RuleFor(d => d.FullName).NotEmpty().MaximumLength(Doctor.FullNameMaxLength);
            doctor.RuleFor(d => d.Specialty).NotEmpty().MaximumLength(Doctor.SpecialtyMaxLength);
        }).When(v => v.NewDoctor is not null);

        RuleFor(v => v.NewPatient!).ChildRules(patient =>
        {
            patient.RuleFor(p => p.FullName).NotEmpty().MaximumLength(Patient.FullNameMaxLength);
            patient.RuleFor(p => p.Email).NotEmpty().EmailAddress().MaximumLength(Patient.EmailMaxLength);
            patient.RuleFor(p => p.Phone).NotEmpty().MaximumLength(Patient.PhoneMaxLength).Matches(Patient.PhonePattern);
        }).When(v => v.NewPatient is not null);
    }
}
