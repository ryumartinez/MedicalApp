using Testcontainers.PostgreSql;
using PostgreSqlTestContainer = Testcontainers.PostgreSql.PostgreSqlContainer;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Database;

/// <summary>
/// Runs PostgreSQL for integration tests.
/// </summary>
public class PostgreSqlContainer : IAsyncDisposable
{
    private readonly PostgreSqlTestContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .WithDatabase("medicalapp_tests")
        .WithUsername("postgres")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .WithAutoRemove(true)
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
