using AppHost.Commands;
using AppHost.Garage;
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
    // The API is its own authorization server. Its HTTPS endpoint is the issuer the browser, the API, and the
    // development mappings all use.
    var apiEndpoint = api.GetEndpoint("https");
    api.WithEnvironment("Authentication__Issuer", apiEndpoint);

    // Shared password for the seeded development accounts. The migration service sets it only on accounts without a password.
    var developmentPassword = builder.AddParameter("dev-account-password",
        new GenerateParameterDefault { MinLength = 16, Special = false }, secret: true, persist: true);

    migrationService
        .WithEnvironment("Authentication__DevelopmentIssuer", apiEndpoint)
        .WithEnvironment("Authentication__DevelopmentPassword", developmentPassword)
        .WithEnvironment("Authentication__Clients__Swagger__RedirectUris__0",
            ReferenceExpression.Create($"{apiEndpoint}/swagger/oauth2-redirect.html"));

    // Garage secrets. Digits only, so each value is valid hexadecimal for the RPC secret and key formats.
    static GenerateParameterDefault Digits(int length) =>
        new() { MinLength = length, Lower = false, Upper = false, Special = false };
    var garageRpcSecret = builder.AddParameter("garage-rpc-secret", Digits(64), secret: true, persist: true);
    var garageAdminToken = builder.AddParameter("garage-admin-token",
        new GenerateParameterDefault { MinLength = 48, Special = false }, secret: true, persist: true);
    var garageKeyId = builder.AddParameter("garage-access-key-id", Digits(24), secret: true, persist: true);
    var garageSecretKey = builder.AddParameter("garage-secret-access-key", Digits(64), secret: true, persist: true);
    const string mediaBucket = "medicalapp-media";
    var corsOrigins = builder.Configuration["Garage:CorsOrigins"] ?? "https://localhost:7255";

    var garage = builder.AddGarage("garage", garageRpcSecret, garageAdminToken,
        metadataVolume: builder.Configuration["Garage:MetadataVolume"],
        dataVolume: builder.Configuration["Garage:DataVolume"]);
    var accessKeyId = ReferenceExpression.Create($"GK{garageKeyId}");

    var garageInitializer = builder.AddProject<GarageInitializer>("garage-init")
        .WithEnvironment("GARAGE_ADMIN_ENDPOINT", garage.Resource.AdminEndpoint)
        .WithEnvironment("GARAGE_S3_ENDPOINT", garage.Resource.S3Endpoint)
        .WithEnvironment("GARAGE_ADMIN_TOKEN", garageAdminToken)
        .WithEnvironment("GARAGE_BUCKET", mediaBucket)
        .WithEnvironment("GARAGE_API_ACCESS_KEY_ID", accessKeyId)
        .WithEnvironment("GARAGE_API_SECRET_ACCESS_KEY", garageSecretKey)
        .WithEnvironment("GARAGE_CORS_ORIGINS", corsOrigins)
        // Garage reports healthy only after the initializer applies the layout.
        .WaitForStart(garage);

    // The API runs on the host, so the published S3 endpoint is reachable by both the API and the browser.
    api.WithEnvironment("Storage__Garage__ServiceUrl", garage.Resource.S3Endpoint)
        .WithEnvironment("Storage__Garage__PublicServiceUrl", garage.Resource.S3Endpoint)
        .WithEnvironment("Storage__Garage__Bucket", mediaBucket)
        .WithEnvironment("Storage__Garage__AccessKeyId", accessKeyId)
        .WithEnvironment("Storage__Garage__SecretAccessKey", garageSecretKey)
        .WaitForCompletion(garageInitializer)
        .WaitFor(garage);
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
