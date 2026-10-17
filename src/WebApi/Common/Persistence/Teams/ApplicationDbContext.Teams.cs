using MedicalApp.Common.Domain.Teams;

// Preserve the namespace across partial classes
// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<Team> Teams => AggregateRootSet<Team>();
}