using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Storage.Garage;

namespace MedicalApp.Common.Storage;

public static class DependencyInjection
{
    /// <summary>
    /// Registers Garage object storage. Missing configuration fails at first use, not at startup.
    /// </summary>
    public static void AddObjectStorage(this IHostApplicationBuilder builder)
    {
        builder.Services.Configure<GarageStorageOptions>(builder.Configuration.GetSection(GarageStorageOptions.SectionName));
        builder.Services.AddSingleton<IObjectStorage, GarageObjectStorage>();
        builder.Services.AddSingleton<ExactContentPromoter>();
        builder.Services.Configure<MediaCleanupOptions>(builder.Configuration.GetSection(MediaCleanupOptions.SectionName));
        builder.Services.AddScoped<MediaCleanup>();
        builder.Services.AddHostedService<MediaCleanupService>();
    }
}
