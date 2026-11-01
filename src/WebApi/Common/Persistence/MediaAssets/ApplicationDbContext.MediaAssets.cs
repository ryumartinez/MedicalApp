using MedicalApp.Common.Domain.MediaAssets;

// ReSharper disable once CheckNamespace
namespace MedicalApp.Common.Persistence;

public partial class ApplicationDbContext
{
    public DbSet<MediaAsset> MediaAssets => Set<MediaAsset>();
}
