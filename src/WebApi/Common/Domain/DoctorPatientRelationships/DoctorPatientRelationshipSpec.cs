using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;

namespace MedicalApp.Common.Domain.DoctorPatientRelationships;

public sealed class DoctorPatientRelationshipSpec : SingleResultSpecification<DoctorPatientRelationship>
{
    public static DoctorPatientRelationshipSpec ByDoctorAndPatient(DoctorId doctorId, PatientId patientId)
    {
        var spec = new DoctorPatientRelationshipSpec();
        spec.Query.Where(relationship => relationship.DoctorId == doctorId && relationship.PatientId == patientId);
        return spec;
    }
}
