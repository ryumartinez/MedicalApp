using System.Net;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MedicalApp.IntegrationTests.Common;

namespace MedicalApp.IntegrationTests.Authentication;

[Collection<ProgressApiCollection>]
public sealed class ProtectedEndpointTests(ProgressApiFixture fixture)
{
    private static readonly string[] ProtectedPrefixes = ["/api/progress-", "/api/media-assets", "/api/access", "/api/accounts"];

    [Fact]
    public async Task EveryProgressOperationRequiresAuthentication()
    {
        var ct = TestContext.Current.CancellationToken;
        using var client = fixture.CreateAnonymousClient();
        var document = JsonNode.Parse(await client.GetStringAsync("/swagger/v1/swagger.json", ct))!;
        var operations = document["paths"]!.AsObject()
            .Where(path => ProtectedPrefixes.Any(path.Key.StartsWith))
            .SelectMany(path => path.Value!.AsObject().Select(operation => (Path: path.Key, Method: operation.Key.ToUpperInvariant(), operation.Value)))
            .ToList();

        operations.Should().HaveCountGreaterThanOrEqualTo(28);
        foreach (var (path, method, operation) in operations)
        {
            operation!["security"]!.AsArray().Should().NotBeEmpty("{0} {1} must declare security", method, path);
            var url = Regex.Replace(path, "{[^}]+}", Guid.CreateVersion7().ToString());
            using var request = new HttpRequestMessage(new HttpMethod(method), url);
            if (method is "POST" or "PUT")
                request.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
            using var response = await client.SendAsync(request, ct);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "{0} {1} must require authentication", method, path);
        }
    }
}
