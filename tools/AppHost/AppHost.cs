using AppHost.Commands;
using AppHost.Keycloak;
using Azure.Provisioning;
using Azure.Provisioning.AppService;
using Projects;

var builder = DistributedApplication.CreateBuilder(args);

// Configure the Azure App Service environment
builder.AddAzureAppServiceEnvironment("plan").ConfigureInfrastructure(infra =>
{
    var plan = infra.GetProvisionableResources()
        .OfType<AppServicePlan>()
        .Single();

    plan.Sku = new AppServiceSkuDescription
    {
        Name = "B1", // Basic tier, 1 core
    };
});

var postgres = builder
    .AddAzurePostgresFlexibleServer("postgres")
    .WithPasswordAuthentication()
    .RunAsContainer(container =>
    {
        container.WithLifetime(ContainerLifetime.Persistent);
        container.WithImageTag("17");
        container.WithDataVolume(builder.Configuration["Postgres:DataVolume"]);
    });

var db = postgres
    .AddDatabase("AppDb", "app-db")
    .WithDropDatabaseCommand();

var migrationService = builder.AddProject<MigrationService>("migrations")
    .PublishAsAzureAppServiceWebsite((_, site) =>
    {
        const string envNetCoreEnvironment = "ASPNETCORE_ENVIRONMENT";

        // Needed for hosted service to run
        site.SiteConfig.IsAlwaysOn = true;

        // Dynamically set environment, so we can enable seeding of data (only happens in 'development')
        var environment = Environment.GetEnvironmentVariable(envNetCoreEnvironment);
        if (string.IsNullOrWhiteSpace(environment))
            return;

        var envSetting = new AppServiceNameValuePair { Name = envNetCoreEnvironment, Value = environment };
        site.SiteConfig.AppSettings.Add(new BicepValue<AppServiceNameValuePair>(envSetting));
    })
    .WithReference(db)
    .WaitFor(postgres);

var api = builder
    .AddProject<WebApi>("api")
    .WithExternalHttpEndpoints()
    .WithReference(db)
    .WaitForCompletion(migrationService);

if (!builder.ExecutionContext.IsPublishMode)
{
    var keycloak = builder.AddKeycloak("keycloak", 8080)
        .WithImageTag(KeycloakImage.Tag)
        .WithDataVolume(builder.Configuration["Keycloak:DataVolume"])
        .WithRealmImport("./Keycloak/Realms")
        .WithEnabledFeatures("persistent-user-sessions")
        .WithOtlpExporter();

    var keycloakInitializer = builder.AddProject<KeycloakInitializer>("keycloak-init")
        .WithEnvironment("KEYCLOAK_ENDPOINT", keycloak.GetEndpoint("http"))
        .WithEnvironment(context =>
        {
            context.EnvironmentVariables["KEYCLOAK_ADMIN_PASSWORD"] = keycloak.Resource.AdminPasswordParameter;
        })
        .WaitFor(keycloak);

    api.WithReference(keycloak).WaitForCompletion(keycloakInitializer);
}

// Configure Application Insights and Log Analytics only if in publish mode
// When running locally, use Aspire Dashboard instead
if (builder.ExecutionContext.IsPublishMode)
{
    var logAnalytics = builder.AddAzureLogAnalyticsWorkspace("log-analytics");
    var insights = builder.AddAzureApplicationInsights("insights", logAnalytics);
    api.WithReference(insights);
    migrationService.WithReference(insights);
}

builder.Build().Run();
