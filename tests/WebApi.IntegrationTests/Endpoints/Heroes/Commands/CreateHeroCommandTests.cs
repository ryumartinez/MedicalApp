using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MedicalApp.Common.Domain.Heroes;
using MedicalApp.Features.Heroes.CreateHero;
using MedicalApp.IntegrationTests.Common;
using System.Net;

namespace MedicalApp.IntegrationTests.Endpoints.Heroes.Commands;

public class CreateHeroCommandTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Command_ShouldCreateHero()
    {
        // Arrange
        (string Name, int PowerLevel)[] powers =
        [
            ("Heat vision", 7),
            ("Super-strength", 10),
            ("Flight", 8),
        ];
        var cmd = new CreateHeroRequest(
            "Clark Kent",
            "Superman",
            powers.Select(p => new CreateHeroRequest.HeroPowerDto(p.Name, p.PowerLevel)));
        var client = GetAnonymousClient();

        // Act
        var result = await client.POSTAsync<CreateHeroEndpoint, CreateHeroRequest, CreateHeroResponse>(cmd);

        // Assert
        result.Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var item = await GetQueryable<Hero>().FirstAsync(CancellationToken);

        item.Should().NotBeNull();
        item.Name.Should().Be(cmd.Name);
        item.Alias.Should().Be(cmd.Alias);
        item.PowerLevel.Should().Be(25);
        item.Powers.Should().HaveCount(3);
        item.CreatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(10));
    }
}