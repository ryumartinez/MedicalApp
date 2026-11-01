using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.DoctorPatientRelationships;

// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ApplicationIdentity> ApplicationIdentities => Set<ApplicationIdentity>();
    public DbSet<DoctorPatientRelationship> DoctorPatientRelationships => Set<DoctorPatientRelationship>();
}
