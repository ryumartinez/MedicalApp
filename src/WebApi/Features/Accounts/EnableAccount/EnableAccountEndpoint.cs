using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts.EnableAccount;

public class EnableAccountEndpoint(ApplicationDbContext dbContext, IIdentityProviderAccounts accounts)
    : EndpointWithoutRequest
{
    public override void Configure()
    {
        Post("/{id:guid}/enable");
        Group<AccountsGroup>();
        Description(x => x.WithName("EnableAccount").ProducesProblemDetails(404).ProducesProblemDetails(409));
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

        var account = await accounts.FindBySubjectAsync(identity.Subject, ct);
        var blocking = AccountProvisioningState.PendingSteps(identity, account)
            .Where(step => !AccountProvisioningState.CompletedByEnable.Contains(step))
            .ToList();
        if (blocking.Count != 0)
        {
            var error = ApplicationIdentityErrors.Incomplete(blocking);
            AddError(error.Description, error.Code);
            await Send.ErrorsAsync(error.ToStatusCode(), ct);
            return;
        }

        // Enable the sign-in account first. The mapping is the last step because it grants progress access.
        if (!account!.Enabled)
            await accounts.SetEnabledAsync(identity.Subject, true, ct);

        if (!identity.Enabled)
        {
            identity.Enable();
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.NoContentAsync(ct);
    }
}
