using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Features.Teams.CreateTeam;
using MedicalApp.IntegrationTests.Common;
using System.Net;

namespace MedicalApp.IntegrationTests.Endpoints.Teams.Commands;

public class CreateTeamCommandTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task Command_ShouldCreateTeam()
    {
        // Arrange
        var cmd = new CreateTeamRequest("Clark Kent");
        var client = GetAnonymousClient();

        // Act
        var result = await client.POSTAsync<CreateTeamEndpoint, CreateTeamRequest>(cmd);

        // Assert
        result.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var item = await GetQueryable<Team>().FirstAsync(CancellationToken);

        item.Should().NotBeNull();
        item.Name.Should().Be(cmd.Name);
        item.CreatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(10));
    }
}