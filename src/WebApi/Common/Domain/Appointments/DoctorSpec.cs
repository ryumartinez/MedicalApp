namespace MedicalApp.Common.Domain.Appointments;

public sealed class DoctorSpec : SingleResultSpecification<Doctor>
{
    public static DoctorSpec ById(DoctorId id)
    {
        var spec = new DoctorSpec();
        spec.Query.Where(e => e.Id == id);
        return spec;
    }
}
