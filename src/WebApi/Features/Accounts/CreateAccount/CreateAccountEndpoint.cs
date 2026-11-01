using System.Security.Claims;
using Ardalis.Specification.EntityFrameworkCore;
using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.Accounts.CreateAccount;

public class CreateAccountEndpoint(ApplicationDbContext dbContext, IIdentityProviderAccounts accounts)
    : Endpoint<CreateAccountRequest, CreateAccountResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<AccountsGroup>();
        Description(x => x.WithName("CreateAccount").ProducesProblemDetails(400).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(CreateAccountRequest req, CancellationToken ct)
    {
        var result = await CreateAsync(req, ct);
        if (result.IsError)
        {
            result.Errors.ForEach(error => AddError(error.Description, error.Code));
            await Send.ErrorsAsync(result.FirstError.ToStatusCode(), ct);
            return;
        }

        await Send.OkAsync(new CreateAccountResponse(result.Value.Id.Value, result.Value.Subject, req.Username), ct);
    }

    private async Task<ErrorOr<ApplicationIdentity>> CreateAsync(CreateAccountRequest req, CancellationToken ct)
    {
        // Every account in the realm shares the administrator's validated issuer.
        var issuer = User.FindFirstValue("iss")!;

        // A retry finds the account from an earlier attempt instead of creating another one.
        var account = await accounts.FindByUsernameAsync(req.Username, ct);
        if (account is null)
        {
            var provisioningId = ApplicationIdentityId.From(Guid.CreateVersion7());
            account = await accounts.CreateAsync(
                new NewIdentityProviderAccount(req.Username, req.Email, req.FirstName, req.LastName),
                provisioningId.Value.ToString(), ct);

            // A conflict means a concurrent request created the username, or the email belongs to another account.
            account ??= await accounts.FindByUsernameAsync(req.Username, ct);
            if (account is null)
                return ApplicationIdentityErrors.EmailInUse;
        }

        // Accounts without the marker were not created here. Linking them by username would bypass trusted provisioning.
        if (!Guid.TryParse(account.ProvisioningId, out var markerId) || markerId == Guid.Empty)
            return ApplicationIdentityErrors.AccountInUse;

        var existing = await FindAsync(issuer, account.Subject, ct);
        if (existing is not null)
            return existing;

        // The mapping starts disabled and grants no progress access until provisioning finishes.
        var identity = ApplicationIdentity.Create(ApplicationIdentityId.From(markerId), issuer, account.Subject);
        dbContext.ApplicationIdentities.Add(identity);
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return identity;
        }
        catch (UniqueConstraintException)
        {
            // A concurrent retry stored the same mapping first.
            dbContext.ChangeTracker.Clear();
            return await FindAsync(issuer, account.Subject, ct) ?? throw new InvalidOperationException("The concurrent mapping is missing.");
        }
    }

    private Task<ApplicationIdentity?> FindAsync(string issuer, string subject, CancellationToken ct) =>
        dbContext.ApplicationIdentities.AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ByIssuerAndSubject(issuer, subject))
            .FirstOrDefaultAsync(ct);
}
