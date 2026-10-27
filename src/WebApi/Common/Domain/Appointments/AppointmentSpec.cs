namespace MedicalApp.Common.Domain.Appointments;

public sealed class AppointmentSpec : SingleResultSpecification<Appointment>
{
    public static AppointmentSpec Overlapping(Doctors.DoctorId doctorId, DateTime startUtc, DateTime endUtc)
    {
        var spec = new AppointmentSpec();
        spec.Query.Where(a => a.DoctorId == doctorId && a.Status != AppointmentStatus.Cancelled &&
            a.StartUtc < endUtc && a.EndUtc > startUtc);
        return spec;
    }

    public static AppointmentSpec Filtered(Patients.PatientId? patientId, Doctors.DoctorId? doctorId, AppointmentStatus? status,
        DateTime? fromUtc, DateTime? toUtc)
    {
        var spec = new AppointmentSpec();
        if (patientId.HasValue)
            spec.Query.Where(a => a.PatientId == patientId.Value);
        if (doctorId.HasValue)
            spec.Query.Where(a => a.DoctorId == doctorId.Value);
        if (status.HasValue)
            spec.Query.Where(a => a.Status == status.Value);
        if (fromUtc.HasValue)
            spec.Query.Where(a => a.StartUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            spec.Query.Where(a => a.StartUtc < toUtc.Value);
        return spec;
    }

    public static AppointmentSpec Page(Patients.PatientId? patientId, Doctors.DoctorId? doctorId, AppointmentStatus? status,
        DateTime? fromUtc, DateTime? toUtc, int page, int pageSize)
    {
        var spec = Filtered(patientId, doctorId, status, fromUtc, toUtc);
        spec.Query.OrderBy(a => a.StartUtc).ThenBy(a => a.Id)
            .Skip((page - 1) * pageSize).Take(pageSize);
        return spec;
    }

    public static AppointmentSpec ById(AppointmentId id)
    {
        var spec = new AppointmentSpec();
        spec.Query.Where(e => e.Id == id)
            .Include(a => a.Patient)
            .Include(a => a.Doctor);
        return spec;
    }
}
