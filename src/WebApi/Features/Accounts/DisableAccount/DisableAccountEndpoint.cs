using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts.DisableAccount;

public class DisableAccountEndpoint(ApplicationDbContext dbContext, IIdentityProviderAccounts accounts)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/{id:guid}/disable");
        Group<AccountsGroup>();
        Description(x => x.WithName("DisableAccount").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var id = ApplicationIdentityId.From(Route<Guid>("id"));
        var identity = await dbContext.ApplicationIdentities
            .WithSpecification(ApplicationIdentitySpec.ById(id))
            .SingleOrDefaultAsync(ct);
        if (identity is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        // Disable the mapping first so the next authorization check denies access even if Keycloak is unavailable.
        if (identity.Enabled)
        {
            identity.Disable();
            await dbContext.SaveChangesAsync(ct);
        }

        // Then stop new tokens. A retry repeats these steps safely.
        var account = await accounts.FindBySubjectAsync(identity.Subject, ct);
        if (account is not null)
        {
            if (account.Enabled)
                await accounts.SetEnabledAsync(identity.Subject, false, ct);
            await accounts.LogoutAsync(identity.Subject, ct);
        }

        await Send.NoContentAsync(ct);
    }
}
