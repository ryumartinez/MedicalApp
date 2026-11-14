using System.Collections.Concurrent;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MigrationService;

namespace MedicalApp.IntegrationTests.Persistence.Seeding;

public sealed class AdministratorBootstrapTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string Issuer = "https://medicalapp.example/";

    [Fact]
    public async Task CreatesTheFirstAdministratorAndPrintsAWorkingLink()
    {
        var logs = new CapturingLogs();

        var (exitCode, output, error) = await RunAsync(["--username", "first-admin", "--email", "first-admin@example.com"], logs);

        exitCode.Should().Be(0, error);
        var link = new Uri(output.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[^1]);
        link.GetLeftPart(UriPartial.Path).Should().Be($"{Issuer}account/set-password");

        await using var provider = Build(logs);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var admin = await users.FindByNameAsync("first-admin");
        admin!.Enabled.Should().BeTrue();
        (await users.HasPasswordAsync(admin)).Should().BeFalse();
        (await users.GetRolesAsync(admin)).Should().Equal(ApiRoles.Administrator);
        var token = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(link.Query)["token"].ToString();
        (await users.ResetPasswordAsync(admin, token, "the first administrator password")).Succeeded.Should().BeTrue();

        logs.Messages.Should().NotContain(message => message.Contains(token) || message.Contains(link.AbsoluteUri));
        (await GetQueryable<MedicalApp.Common.Domain.ApplicationIdentities.ApplicationIdentity>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task RefusesASecondBootstrapUnlessForced()
    {
        (await RunAsync(["--username", "first-admin", "--email", "first-admin@example.com"])).ExitCode.Should().Be(0);

        var (refused, refusedOutput, refusedError) = await RunAsync(["--username", "second-admin", "--email", "second-admin@example.com"]);
        refused.Should().Be(1);
        refusedOutput.Should().BeEmpty();
        refusedError.Should().Contain("already exists");
        (await GetQueryable<ApplicationUser>().AnyAsync(x => x.UserName == "second-admin", CancellationToken)).Should().BeFalse();

        var (forced, _, forcedError) = await RunAsync(["--username", "second-admin", "--email", "second-admin@example.com", "--force"]);
        forced.Should().Be(0, forcedError);
        (await GetQueryable<ApplicationUser>().AnyAsync(x => x.UserName == "second-admin", CancellationToken)).Should().BeTrue();
    }

    [Fact]
    public async Task RefusesAnExistingUsername()
    {
        (await RunAsync(["--username", "first-admin", "--email", "first-admin@example.com"])).ExitCode.Should().Be(0);

        var (exitCode, _, error) = await RunAsync(["--username", "first-admin", "--email", "other@example.com", "--force"]);

        exitCode.Should().Be(1);
        error.Should().Contain("already in use");
        (await GetQueryable<ApplicationUser>().CountAsync(CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData]
    [InlineData("--username", "only-name")]
    [InlineData("--email", "only@example.com")]
    public void RequiresUsernameAndEmail(params string[] args) =>
        AdministratorBootstrap.Parse(args).Should().BeNull();

    [Fact]
    public async Task RequiresAProductionIssuerBeforeAnyChange()
    {
        var run = () => RunAsync(["--username", "first-admin", "--email", "first-admin@example.com"], issuer: null);

        await run.Should().ThrowAsync<InvalidOperationException>().WithMessage($"*{IdentityIssuer.ConfigurationKey}*");
        (await GetQueryable<ApplicationUser>().CountAsync(CancellationToken)).Should().Be(0);
    }

    private async Task<(int ExitCode, string Output, string Error)> RunAsync(string[] args, CapturingLogs? logs = null, string? issuer = Issuer)
    {
        await using var provider = Build(logs ?? new CapturingLogs(), issuer);
        using var scope = provider.CreateScope();
        using var output = new StringWriter();
        using var error = new StringWriter();
        var exitCode = await scope.ServiceProvider.GetRequiredService<AdministratorBootstrap>()
            .RunAsync(AdministratorBootstrap.Parse(args)!, output, error, CancellationToken);
        return (exitCode, output.ToString(), error.ToString());
    }

    private ServiceProvider Build(CapturingLogs logs, string? issuer = Issuer)
    {
        string connectionString;
        using (var scope = CreateScope())
            connectionString = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
        var settings = new Dictionary<string, string?> { [IdentityIssuer.ConfigurationKey] = issuer };
        return MigrationServices.Build(connectionString, Environments.Production, settings, logs);
    }

    private sealed class CapturingLogs : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) => new Logger(Messages);

        public void Dispose()
        {
        }

        private sealed class Logger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception));
        }
    }
}
