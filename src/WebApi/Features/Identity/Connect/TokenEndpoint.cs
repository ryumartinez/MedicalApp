using Microsoft.AspNetCore;
using MedicalApp.Common.Identity;
using MedicalApp.Host.Extensions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace MedicalApp.Features.Identity.Connect;

/// <summary>
/// Exchanges an authorization code. The account must still be able to sign in, and tokens carry its current roles.
/// </summary>
public class TokenEndpoint(
    UserManager<ApplicationUser> users,
    AppSignInManager signIn,
    IdentityPrincipals principals) : ConnectEndpoint
{
    public override void Configure()
    {
        ConfigureConnect(IdentityServerExt.TokenPath);
        Verbs(Http.POST);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");
        if (!request.IsAuthorizationCodeGrantType())
        {
            await ForbidAsync(Errors.UnsupportedGrantType, "Only the authorization code grant is supported.");
            return;
        }

        var code = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var subject = code.Principal?.GetClaim(OpenIddictConstants.Claims.Subject);
        var user = subject is null ? null : await users.FindByIdAsync(subject);
        if (user is null || !await signIn.CanSignInAsync(user))
        {
            await ForbidAsync(Errors.InvalidGrant, "The account can no longer sign in.");
            return;
        }

        var principal = await principals.CreateAsync(user, code.Principal!.GetScopes());
        await Send.ResultAsync(Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }
}
