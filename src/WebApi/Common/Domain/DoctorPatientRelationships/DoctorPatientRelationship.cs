using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Common.Domain.DoctorPatientRelationships;

[ValueObject<Guid>]
public readonly partial struct DoctorPatientRelationshipId;

public class DoctorPatientRelationship : Entity<DoctorPatientRelationshipId>
{
    public DoctorId DoctorId
    {
        get;
        private set
        {
            if (value.Value == Guid.Empty)
                throw new ArgumentException("Doctor ID is required.", nameof(DoctorId));
            field = value;
        }
    }

    public PatientId PatientId
    {
        get;
        private set
        {
            if (value.Value == Guid.Empty)
                throw new ArgumentException("Patient ID is required.", nameof(PatientId));
            field = value;
        }
    }
    public bool IsActive { get; private set; }

    private DoctorPatientRelationship() { }

    public static DoctorPatientRelationship Create(DoctorId doctorId, PatientId patientId)
    {
        return new DoctorPatientRelationship
        {
            Id = DoctorPatientRelationshipId.From(Guid.CreateVersion7()),
            DoctorId = doctorId,
            PatientId = patientId,
            IsActive = true
        };
    }

    public void Revoke() => IsActive = false;
    public void Reactivate() => IsActive = true;
}
