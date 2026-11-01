using MedicalApp.Common.Authorization;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Services;

namespace MedicalApp.Host;

public static class DependencyInjection
{
    public static void AddWebApi(this IHostApplicationBuilder builder)
    {
        var services = builder.Services;

        services.AddHttpContextAccessor();

        services.AddScoped<ICurrentUserService, CurrentUserService>();
        services.AddScoped<ICurrentIdentityService, CurrentIdentityService>();
        services.AddScoped<ProgressAuthorizationService>();

        services.AddOpenApi();

        services.AddFastEndpoints();
    }

    public static void AddApplication(this IHostApplicationBuilder builder)
    {
        var applicationAssembly = typeof(DependencyInjection).Assembly;
        var services = builder.Services;

        services.AddValidatorsFromAssembly(applicationAssembly, includeInternalTypes: true);
    }
}