using MedicalApp.Common.Identity;
using MedicalApp.Host.Extensions;
using Microsoft.AspNetCore.Authentication;
using OpenIddict.Server.AspNetCore;

namespace MedicalApp.Features.Identity.Connect;

/// <summary>
/// Ends the sign-in session. OpenIddict redirects to the client's registered post-logout URI when one is given.
/// </summary>
public class EndSessionEndpoint(AppSignInManager signIn) : ConnectEndpoint
{
    public override void Configure() => ConfigureConnect(IdentityServerExt.EndSessionPath);

    public override async Task HandleAsync(CancellationToken ct)
    {
        await signIn.SignOutAsync();
        await Send.ResultAsync(Results.SignOut(
            new AuthenticationProperties { RedirectUri = IdentityServerExt.SignedOutPath },
            [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]));
    }
}
