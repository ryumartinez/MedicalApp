using MedicalApp.Common.Authentication;
using MedicalApp.Common.Identity;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MigrationService.Initializers;

namespace MedicalApp.IntegrationTests.Authentication;

public sealed class DevelopmentAccountPasswordTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private const string ChangedPassword = "changed-by-developer-1";
    private const string OtherSecret = "another-development-secret";

    [Fact]
    public async Task CreatesAccountsWithRolesAndTheSharedPassword()
    {
        await SeedAsync(MigrationServices.DevelopmentPassword);

        await using var provider = Build(MigrationServices.DevelopmentPassword);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        foreach (var account in DevelopmentAccountInitializer.Accounts)
        {
            var user = await users.FindByIdAsync(account.Id.ToString());
            user!.UserName.Should().Be(account.Username);
            user.Enabled.Should().BeTrue();
            (await users.GetRolesAsync(user)).Should().Equal(account.Role);
            (await users.CheckPasswordAsync(user, MigrationServices.DevelopmentPassword)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task KeepsPasswordThatDeveloperChanged()
    {
        await SeedAsync(MigrationServices.DevelopmentPassword);
        await WithUserAsync(DevelopmentAccountInitializer.PatientUserId, async (users, user) =>
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            (await users.ResetPasswordAsync(user, token, ChangedPassword)).Succeeded.Should().BeTrue();
        });

        await SeedAsync(MigrationServices.DevelopmentPassword);

        await WithUserAsync(DevelopmentAccountInitializer.PatientUserId, async (users, user) =>
        {
            (await users.CheckPasswordAsync(user, ChangedPassword)).Should().BeTrue();
            (await users.CheckPasswordAsync(user, MigrationServices.DevelopmentPassword)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task ChangedSecretDoesNotOverwriteExistingPasswords()
    {
        await SeedAsync(MigrationServices.DevelopmentPassword);

        await SeedAsync(OtherSecret);

        await WithUserAsync(DevelopmentAccountInitializer.DoctorUserId, async (users, user) =>
        {
            (await users.CheckPasswordAsync(user, MigrationServices.DevelopmentPassword)).Should().BeTrue();
            (await users.CheckPasswordAsync(user, OtherSecret)).Should().BeFalse();
        });
    }

    [Fact]
    public async Task GivesPasswordToDevelopmentAccountWithoutOne()
    {
        await SeedAsync(password: null);
        await WithUserAsync(DevelopmentAccountInitializer.AdministratorUserId, async (users, user) =>
            (await users.HasPasswordAsync(user)).Should().BeFalse());

        await SeedAsync(OtherSecret);

        await WithUserAsync(DevelopmentAccountInitializer.AdministratorUserId, async (users, user) =>
            (await users.CheckPasswordAsync(user, OtherSecret)).Should().BeTrue());
    }

    [Fact]
    public async Task LeavesProvisionedAccountWithoutPassword()
    {
        var provisionedId = Guid.CreateVersion7();
        await using (var provider = Build(MigrationServices.DevelopmentPassword))
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.CreateAsync(new ApplicationUser { Id = provisionedId, UserName = "provisioned-patient", Email = "provisioned@example.invalid" }))
                .Succeeded.Should().BeTrue();
        }

        await SeedAsync(MigrationServices.DevelopmentPassword);

        await WithUserAsync(provisionedId, async (users, user) => (await users.HasPasswordAsync(user)).Should().BeFalse());
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("12345678901")]
    public async Task RejectsShortPasswordBeforeAnyChange(string password)
    {
        var seed = () => SeedAsync(password);

        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*development account password*");
        (await GetQueryable<ApplicationUser>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task FailsWithoutChangesWhenUsernameBelongsToAnotherAccount()
    {
        var otherId = Guid.CreateVersion7();
        await using (var provider = Build(MigrationServices.DevelopmentPassword))
        using (var scope = provider.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            (await users.CreateAsync(new ApplicationUser { Id = otherId, UserName = "development-doctor", Email = "squatter@example.invalid" }))
                .Succeeded.Should().BeTrue();
        }

        var seed = () => SeedAsync(MigrationServices.DevelopmentPassword);

        await seed.Should().ThrowAsync<InvalidOperationException>().WithMessage("*development-doctor*");
        var squatter = await GetQueryable<ApplicationUser>().SingleAsync(x => x.Id == otherId, CancellationToken);
        squatter.PasswordHash.Should().BeNull();
        (await GetQueryable<ApplicationUser>().AnyAsync(x => x.Id == DevelopmentAccountInitializer.DoctorUserId, CancellationToken)).Should().BeFalse();
    }

    [Fact]
    public async Task ProductionCreatesNoDevelopmentAccounts()
    {
        await MigrationServices.RunWorkerAsync(ConnectionString, Environments.Production, MigrationServices.DevelopmentSettings(), CancellationToken);

        (await GetQueryable<ApplicationUser>().CountAsync(CancellationToken)).Should().Be(0);
        (await GetQueryable<IdentityRole<Guid>>().CountAsync(CancellationToken)).Should().Be(ApiRoles.All.Count);
    }

    private async Task SeedAsync(string? password)
    {
        await using var provider = Build(password);
        using var scope = provider.CreateScope();
        // Roles come from the client initializer, which runs first in every environment.
        await scope.ServiceProvider.GetRequiredService<IdentityClientInitializer>().InitializeAsync(CancellationToken);
        await scope.ServiceProvider.GetRequiredService<DevelopmentAccountInitializer>().SeedAsync(CancellationToken);
    }

    private async Task WithUserAsync(Guid id, Func<UserManager<ApplicationUser>, ApplicationUser, Task> action)
    {
        await using var provider = Build(MigrationServices.DevelopmentPassword);
        using var scope = provider.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        await action(users, (await users.FindByIdAsync(id.ToString()))!);
    }

    private ServiceProvider Build(string? password)
    {
        var settings = MigrationServices.DevelopmentSettings();
        settings[DevelopmentAccountInitializer.PasswordKey] = password;
        return MigrationServices.Build(ConnectionString, Environments.Development, settings);
    }

    private string ConnectionString
    {
        get
        {
            using var scope = CreateScope();
            return scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.GetConnectionString()!;
        }
    }
}
