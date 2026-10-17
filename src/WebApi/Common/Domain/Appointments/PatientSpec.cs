namespace MedicalApp.Common.Domain.Appointments;

public sealed class PatientSpec : SingleResultSpecification<Patient>
{
    public static PatientSpec ById(PatientId id)
    {
        var spec = new PatientSpec();
        spec.Query.Where(e => e.Id == id);
        return spec;
    }
}
