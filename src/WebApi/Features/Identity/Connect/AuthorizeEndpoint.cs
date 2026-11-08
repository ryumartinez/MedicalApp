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
/// Continues an authorization request after OpenIddict validated the client, redirect URI, scopes, and PKCE.
/// Signs the user in through the sign-in page when there is no valid sign-in session.
/// </summary>
public class AuthorizeEndpoint(
    UserManager<ApplicationUser> users,
    AppSignInManager signIn,
    IdentityPrincipals principals) : ConnectEndpoint
{
    public override void Configure() => ConfigureConnect(IdentityServerExt.AuthorizationPath);

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("The OpenID Connect request cannot be retrieved.");

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var user = session.Succeeded ? await users.GetUserAsync(session.Principal!) : null;
        if (user is null || request.HasPromptValue(PromptValues.Login) || !await signIn.CanSignInAsync(user))
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                await ForbidAsync(Errors.LoginRequired, "The user is not signed in.");
                return;
            }

            // Return here with the same parameters once the user signs in. Prompt=login is dropped to avoid a loop.
            var parameters = HttpContext.Request.HasFormContentType ? HttpContext.Request.Form.ToList() : HttpContext.Request.Query.ToList();
            parameters.RemoveAll(parameter => parameter.Key == Parameters.Prompt);
            await Send.ResultAsync(Results.Challenge(new AuthenticationProperties
            {
                RedirectUri = HttpContext.Request.PathBase + HttpContext.Request.Path + QueryString.Create(parameters)
            }, [IdentityConstants.ApplicationScheme]));
            return;
        }

        var principal = await principals.CreateAsync(user, request.GetScopes());
        await Send.ResultAsync(Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme));
    }
}
