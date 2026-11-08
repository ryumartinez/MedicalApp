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
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Web;

/// <summary>
/// Host builder (services, DI and configuration) for integration tests
/// </summary>
public class WebApiTestFactory : WebApplicationFactory<IWebApiMarker>
{
    private readonly DbConnection _dbConnection;
    private readonly GarageContainer? _garage;
    private readonly TimeProvider? _timeProvider;

    public WebApiTestFactory(DbConnection dbConnection, GarageContainer? garage = null, TimeProvider? timeProvider = null)
    {
        _dbConnection = dbConnection;
        _garage = garage;
        _timeProvider = timeProvider;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:AppDb", _dbConnection.ConnectionString);

        // The API is its own authorization server. Tests use throwaway keys and the test server's HTTPS address as issuer.
        builder.UseSetting(IdentityIssuer.ConfigurationKey, TestIdentity.Issuer);
        builder.UseSetting(ServerCredentials.EphemeralKeysKey, "true");

        // A retired signing key stays configured after the new one, as during a certificate rotation.
        builder.ConfigureTestServices(services => services.Configure<OpenIddictServerOptions>(options =>
            options.SigningCredentials.Add(new SigningCredentials(TestIdentity.RetiredSigningKey, SecurityAlgorithms.RsaSha256))));

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
    }
}
