using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Persistence;
using MedicalApp.Common.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using MigrationService;
using MigrationService.Initializers;
using NSubstitute;

namespace MedicalApp.IntegrationTests.Common;

/// <summary>
/// Builds the migration service's services against a test database, as its Program does.
/// </summary>
public static class MigrationServices
{
    public const string DevelopmentIssuer = "https://localhost:7255/";
    public const string DevelopmentPassword = "development-password-1";
    public const string SwaggerRedirectUri = "https://localhost:7255/swagger/oauth2-redirect.html";

    public static Dictionary<string, string?> DevelopmentSettings() => new()
    {
        [ProgressIdentityInitializer.IssuerKey] = DevelopmentIssuer,
        [DevelopmentAccountInitializer.PasswordKey] = DevelopmentPassword,
        [$"{IdentityClientInitializer.SwaggerRedirectUrisKey}:0"] = SwaggerRedirectUri
    };

    public static ServiceProvider Build(string connectionString, string environment, IDictionary<string, string?>? settings = null,
        ILoggerProvider? logs = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings ?? DevelopmentSettings()).Build();
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(LogLevel.Trace);
            if (logs is not null)
                logging.AddProvider(logs);
        });
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new HostingEnvironment { EnvironmentName = environment, ApplicationName = "MigrationService" });
        services.AddSingleton<ICurrentUserService, MigrationUserService>();
        services.AddSingleton<TimeProvider>(DatabaseTimeProvider.Instance);
        services.AddDbContext<ApplicationDbContext>((provider, options) => options
            .UseNpgsql(connectionString)
            .AddInterceptors(new EntitySaveChangesInterceptor(provider.GetRequiredService<ICurrentUserService>(), DatabaseTimeProvider.Instance)));
        services.AddScoped<ApplicationDbContextInitializer>();
        services.AddScoped<IdentityClientInitializer>();
        services.AddScoped<DevelopmentAccountInitializer>();
        services.AddScoped<ProgressIdentityInitializer>();
        services.AddScoped<ProgressWorkflowInitializer>();
        services.AddScoped<AdministratorBootstrap>();
        services.AddIdentityStores(configuration);

        // Validate like the real host does in Development, so missing registrations fail here too.
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static async Task RunWorkerAsync(string connectionString, string environment, IDictionary<string, string?>? settings,
        CancellationToken cancellationToken)
    {
        await using var provider = Build(connectionString, environment, settings);
        var worker = new Worker(provider, Substitute.For<IHostApplicationLifetime>(), provider.GetRequiredService<ILogger<Worker>>());
        await worker.StartAsync(cancellationToken);
        await worker.ExecuteTask!;
    }
}
