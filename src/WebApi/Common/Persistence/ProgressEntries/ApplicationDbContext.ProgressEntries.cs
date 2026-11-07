using MedicalApp.Common.Domain.ProgressEntries;

// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ProgressEntry> ProgressEntries => Set<ProgressEntry>();
}
