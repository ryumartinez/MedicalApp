using Microsoft.EntityFrameworkCore;
using Npgsql;
using Respawn;
using MedicalApp.Common.Persistence;
using System.Data.Common;

namespace MedicalApp.IntegrationTests.Common.Infrastructure.Database;

/// <summary>
/// Manages the schema and data for the database container.
/// </summary>
public class TestDatabase : IAsyncDisposable
{
    private readonly PostgreSqlContainer _postgres = new();
    private Respawner _checkpoint = null!;

    public async Task InitializeAsync()
    {
        await _postgres.InitializeAsync();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.ConnectionString)
            .Options;

        await using var dbContext = new ApplicationDbContext(options);
        await dbContext.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        _checkpoint = await Respawner.CreateAsync(connection,
            new RespawnerOptions
            {
                DbAdapter = DbAdapter.Postgres,
                SchemasToInclude = ["public"],
                TablesToIgnore = ["__EFMigrationsHistory"]
            });
    }

    public DbConnection DbConnection => new NpgsqlConnection(_postgres.ConnectionString);

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(_postgres.ConnectionString);
        await connection.OpenAsync();
        await _checkpoint.ResetAsync(connection);
    }

    public async ValueTask DisposeAsync()
    {
        await _postgres.DisposeAsync();
    }
}
