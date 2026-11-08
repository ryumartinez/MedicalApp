using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts.GetAccount;

public class GetAccountEndpoint(ApplicationDbContext dbContext, IIdentityProviderAccounts accounts)
    : Endpoint<GetAccountRequest, GetAccountResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<AccountsGroup>();
        Description(x => x.WithName("GetAccount").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(GetAccountRequest req, CancellationToken ct)
    {
        var identity = await dbContext.ApplicationIdentities.AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ById(ApplicationIdentityId.From(req.Id)))
            .SingleOrDefaultAsync(ct);
        if (identity is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var account = await accounts.FindBySubjectAsync(identity.Subject, ct);
        await Send.OkAsync(new GetAccountResponse(
            identity.Id.Value,
            identity.Issuer,
            identity.Subject,
            identity.Enabled,
            identity.DoctorId?.Value,
            identity.PatientId?.Value,
            account is null
                ? null
                : new GetAccountResponse.ProviderAccountDto(account.Username, account.Enabled, account.HasPassword, account.ApiRoles.Order().ToList()),
            AccountProvisioningState.PendingSteps(identity, account)), ct);
    }
}
