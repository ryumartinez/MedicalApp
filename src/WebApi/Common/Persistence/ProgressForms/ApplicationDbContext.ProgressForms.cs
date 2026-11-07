using MedicalApp.Common.Domain.ProgressForms;

// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ProgressForm> ProgressForms => Set<ProgressForm>();
    public DbSet<ProgressFormVersion> ProgressFormVersions => Set<ProgressFormVersion>();
}
