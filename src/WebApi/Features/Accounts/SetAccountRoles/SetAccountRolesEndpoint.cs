using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts.SetAccountRoles;

public class SetAccountRolesEndpoint(ApplicationDbContext dbContext, IIdentityProviderAccounts accounts)
    : Endpoint<SetAccountRolesRequest>
{
    public override void Configure()
    {
        Put("/{id}/roles");
        Group<AccountsGroup>();
        Description(x => x.WithName("SetAccountRoles")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(SetAccountRolesRequest req, CancellationToken ct)
    {
        var identity = await dbContext.ApplicationIdentities.AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ById(ApplicationIdentityId.From(req.Id)))
            .SingleOrDefaultAsync(ct);
        if (identity is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (await accounts.FindBySubjectAsync(identity.Subject, ct) is null)
        {
            AddError(ApplicationIdentityErrors.ProviderAccountMissing.Description, ApplicationIdentityErrors.ProviderAccountMissing.Code);
            await Send.ErrorsAsync(StatusCodes.Status409Conflict, ct);
            return;
        }

        await accounts.SetApiRolesAsync(identity.Subject, req.Roles.ToHashSet(StringComparer.Ordinal), ct);
        await Send.NoContentAsync(ct);
    }
}
