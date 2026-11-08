using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using OpenIddict.Server;

namespace MedicalApp.Features.Accounts.CreatePasswordSetupLink;

public class CreatePasswordSetupLinkEndpoint(
    ApplicationDbContext dbContext,
    UserManager<ApplicationUser> users,
    IOptions<PasswordSetupTokenProviderOptions> tokenOptions,
    IOptions<OpenIddictServerOptions> serverOptions,
    TimeProvider timeProvider)
    : EndpointWithoutRequest<CreatePasswordSetupLinkResponse>
{
    public const string SetPasswordPath = "account/set-password";

    public override void Configure()
    {
        Post("/{id:guid}/password-setup-link");
        Group<AccountsGroup>();
        Description(x => x.WithName("CreatePasswordSetupLink").ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var identity = await dbContext.ApplicationIdentities.AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ById(ApplicationIdentityId.From(Route<Guid>("id"))))
            .SingleOrDefaultAsync(ct);
        if (identity is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var user = Guid.TryParse(identity.Subject, out var userId) ? await users.FindByIdAsync(userId.ToString()) : null;
        if (user is null)
        {
            AddError(ApplicationIdentityErrors.ProviderAccountMissing.Description, ApplicationIdentityErrors.ProviderAccountMissing.Code);
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        var link = await CreateLinkAsync(users, user, serverOptions.Value.Issuer!, tokenOptions.Value.TokenLifespan, timeProvider);
        await Send.OkAsync(link, ct);
    }

    /// <summary>
    /// Changes the security stamp first, which invalidates earlier links and ends sign-in sessions, then issues the token.
    /// The migration service's administrator bootstrap uses the same steps.
    /// </summary>
    public static async Task<CreatePasswordSetupLinkResponse> CreateLinkAsync(UserManager<ApplicationUser> users, ApplicationUser user,
        Uri issuer, TimeSpan lifetime, TimeProvider timeProvider)
    {
        var stamp = await users.UpdateSecurityStampAsync(user);
        if (!stamp.Succeeded)
            throw new InvalidOperationException(string.Join(" ", stamp.Errors.Select(error => error.Description)));

        var token = await users.GeneratePasswordResetTokenAsync(user);
        var url = new Uri(issuer, $"{SetPasswordPath}?user={user.Id:D}&token={Uri.EscapeDataString(token)}");
        return new CreatePasswordSetupLinkResponse(url, timeProvider.GetUtcNow().Add(lifetime));
    }
}
