using MedicalApp.Common.Domain.ProgressPlans;

// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<ProgressPlan> ProgressPlans => Set<ProgressPlan>();
    public DbSet<ProgressPlanGrant> ProgressPlanGrants => Set<ProgressPlanGrant>();
}
