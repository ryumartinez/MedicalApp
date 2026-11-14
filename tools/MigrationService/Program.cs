using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationService;
using MigrationService.Initializers;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using MedicalApp.Common.Persistence;
using MedicalApp.Common.Persistence.Interceptors;

// "bootstrap-admin" creates the first administrator instead of migrating. See AdministratorBootstrap.
var bootstrap = args.Length > 0 && args[0] == AdministratorBootstrap.Command;

// The command's own flags are not configuration, so they stay out of the host's command-line provider.
var builder = Host.CreateApplicationBuilder(bootstrap ? [] : args);

builder.AddServiceDefaults();

if (!bootstrap)
    builder.Services.AddHostedService<Worker>();

builder.Services
    .AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(Worker.ActivitySourceName));

builder.Services.AddScoped<ApplicationDbContextInitializer>();
builder.Services.AddScoped<IdentityClientInitializer>();
builder.Services.AddScoped<DevelopmentAccountInitializer>();
builder.Services.AddScoped<ProgressIdentityInitializer>();
builder.Services.AddScoped<ProgressWorkflowInitializer>();
builder.Services.AddScoped<AdministratorBootstrap>();
builder.Services.AddScoped<EntitySaveChangesInterceptor>();
builder.Services.AddScoped<ICurrentUserService, MigrationUserService>();
builder.Services.AddSingleton(TimeProvider.System);

builder.AddNpgsqlDbContext<ApplicationDbContext>("AppDb",
    null,
    options =>
    {
        var serviceProvider = builder.Services.BuildServiceProvider();
        options.AddInterceptors(
            serviceProvider.GetRequiredService<EntitySaveChangesInterceptor>());
    });

builder.Services.AddIdentityStores(builder.Configuration);

var host = builder.Build();

if (bootstrap)
{
    var arguments = AdministratorBootstrap.Parse(args.Skip(1).ToList());
    if (arguments is null)
    {
        await Console.Error.WriteLineAsync(AdministratorBootstrap.Usage);
        return 2;
    }

    using var scope = host.Services.CreateScope();
    return await scope.ServiceProvider.GetRequiredService<AdministratorBootstrap>()
        .RunAsync(arguments, Console.Out, Console.Error, CancellationToken.None);
}

await host.RunAsync();
return 0;