using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Common.Domain.ApplicationIdentities;

[ValueObject<Guid>]
public readonly partial struct ApplicationIdentityId;

public class ApplicationIdentity : Entity<ApplicationIdentityId>
{
    public string Issuer
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(Issuer));
            field = value;
        }
    } = null!;

    public string Subject
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(Subject));
            field = value;
        }
    } = null!;

    public bool Enabled { get; private set; }
    public DoctorId? DoctorId
    {
        get;
        private set
        {
            if (value is { } id && id.Value == Guid.Empty)
                throw new ArgumentException("Doctor ID is required.", nameof(DoctorId));
            field = value;
        }
    }

    public PatientId? PatientId
    {
        get;
        private set
        {
            if (value is { } id && id.Value == Guid.Empty)
                throw new ArgumentException("Patient ID is required.", nameof(PatientId));
            field = value;
        }
    }

    private ApplicationIdentity() { }

    public static ApplicationIdentity Create(string issuer, string subject) =>
        Create(ApplicationIdentityId.From(Guid.CreateVersion7()), issuer, subject);

    /// <summary>
    /// Creates a disabled identity with an ID chosen earlier, such as the provisioning marker on the identity provider account.
    /// </summary>
    public static ApplicationIdentity Create(ApplicationIdentityId id, string issuer, string subject)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentException("Identity ID is required.", nameof(id));

        return new ApplicationIdentity
        {
            Id = id,
            Issuer = issuer,
            Subject = subject
        };
    }

    public void LinkDoctor(DoctorId doctorId)
    {
        EnsureDisabled();
        DoctorId = doctorId;
    }

    public void LinkPatient(PatientId patientId)
    {
        EnsureDisabled();
        PatientId = patientId;
    }

    public void Enable()
    {
        if (DoctorId is null && PatientId is null)
            throw new InvalidOperationException("An identity requires a doctor or patient mapping before it can be enabled.");
        Enabled = true;
    }

    public void Disable() => Enabled = false;

    private void EnsureDisabled()
    {
        if (Enabled)
            throw new InvalidOperationException("Disable the identity before changing its mappings.");
    }
}
