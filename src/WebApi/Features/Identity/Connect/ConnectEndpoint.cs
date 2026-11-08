using Microsoft.AspNetCore.Authentication;
using OpenIddict.Server.AspNetCore;

namespace MedicalApp.Features.Identity.Connect;

/// <summary>
/// Shared setup for the OpenIddict passthrough endpoints: no API prefix, anonymous, and hidden from Swagger.
/// </summary>
public abstract class ConnectEndpoint : EndpointWithoutRequest
{
    protected void ConfigureConnect(string route)
    {
        Verbs(Http.GET, Http.POST);
        Routes(route);
        RoutePrefixOverride(string.Empty);
        AllowAnonymous();
        Description(x => x.ExcludeFromDescription());
    }

    /// <summary>
    /// Returns an OAuth error through OpenIddict, which formats it for the client.
    /// </summary>
    protected Task ForbidAsync(string error, string description) =>
        Send.ResultAsync(Results.Forbid(
            new AuthenticationProperties(new Dictionary<string, string?>
            {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description
            }),
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]));
}
