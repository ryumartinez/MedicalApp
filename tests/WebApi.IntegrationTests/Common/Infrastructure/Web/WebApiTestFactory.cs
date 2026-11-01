using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Interfaces;
using MedicalApp.IntegrationTests.Common.Infrastructure.Identity;
using MedicalApp.IntegrationTests.Common.Infrastructure.Storage;
using MedicalApp.Common.Storage;
using MedicalApp.Common.Storage.Garage;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Data.Common;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Web;

/// <summary>
/// Host builder (services, DI and configuration) for integration tests
/// </summary>
public class WebApiTestFactory : WebApplicationFactory<IWebApiMarker>
{
    private readonly DbConnection _dbConnection;
    private readonly KeycloakContainer? _keycloak;
    private readonly GarageContainer? _garage;
    private readonly TimeProvider? _timeProvider;

    public WebApiTestFactory(DbConnection dbConnection, KeycloakContainer? keycloak = null, GarageContainer? garage = null,
        TimeProvider? timeProvider = null)
    {
        _dbConnection = dbConnection;
        _keycloak = keycloak;
        _garage = garage;
        _timeProvider = timeProvider;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AppDb", _dbConnection.ConnectionString);

        // Tests run cleanup explicitly.
        builder.UseSetting($"{MediaCleanupOptions.SectionName}:Enabled", "false");
        if (_timeProvider is not null)
            builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton(_timeProvider)));
        if (_garage is not null)
        {
            builder.UseSetting($"{GarageStorageOptions.SectionName}:ServiceUrl", _garage.S3Endpoint.ToString());
            builder.UseSetting($"{GarageStorageOptions.SectionName}:Bucket", GarageContainer.Bucket);
            builder.UseSetting($"{GarageStorageOptions.SectionName}:AccessKeyId", _garage.AccessKeyId);
            builder.UseSetting($"{GarageStorageOptions.SectionName}:SecretAccessKey", _garage.SecretAccessKey);
        }

        if (_keycloak is null)
            return;

        // The API discovers Keycloak the same way Aspire configures it, and trusts only the test certificate.
        builder.UseSetting("services:keycloak:https:0", _keycloak.Address);
        builder.UseSetting($"{KeycloakProvisioningOptions.SectionName}:ClientSecret", _keycloak.ProvisioningSecret);
        builder.ConfigureTestServices(services =>
        {
            services.AddHttpClient("KeycloakBackchannel").ConfigurePrimaryHttpMessageHandler(_keycloak.CreateHandler);
            services.AddHttpClient(KeycloakAccountAdministration.HttpClientName).ConfigurePrimaryHttpMessageHandler(_keycloak.CreateHandler);
        });
    }
}
