using EntityFramework.Exceptions.SqlServer;
using MedicalApp.Common.Persistence.Interceptors;

namespace MedicalApp.Common.Persistence;

public static class DependencyInjection
{
    public static void AddInfrastructure(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddScoped<EntitySaveChangesInterceptor>();
        services.AddScoped<DispatchDomainEventsInterceptor>();
        services.AddSingleton(TimeProvider.System);

        builder.AddSqlServerDbContext<ApplicationDbContext>("AppDb",
            null,
            options =>
            {
                var serviceProvider = builder.Services.BuildServiceProvider();

                options.AddInterceptors(
                    serviceProvider.GetRequiredService<EntitySaveChangesInterceptor>(),
                    serviceProvider.GetRequiredService<DispatchDomainEventsInterceptor>());

                // Return strongly typed useful exceptions
                options.UseExceptionProcessor();
            });
    }
}