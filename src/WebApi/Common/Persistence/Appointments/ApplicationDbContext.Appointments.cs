using MedicalApp.Common.Domain.Appointments;

// Preserve the namespace across partial classes
// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<Appointment> Appointments => AggregateRootSet<Appointment>();
    public DbSet<Doctor> Doctors => Set<Doctor>();
    public DbSet<Patient> Patients => Set<Patient>();
}
