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
        // Every account shares the administrator's validated issuer: this server.
        var issuer = User.FindFirstValue("iss")!;

        try
        {
            return await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => CreateInTransactionAsync(issuer, req, ct));
        }
        catch (UniqueConstraintException)
        {
            // A concurrent retry stored the same account or mapping first.
            dbContext.ChangeTracker.Clear();
            return await FindExistingAsync(issuer, req.Username, ct);
        }
    }

    /// <summary>
    /// Creates the sign-in account and its disabled mapping together, or neither.
    /// </summary>
    private async Task<ErrorOr<ApplicationIdentity>> CreateInTransactionAsync(string issuer, CreateAccountRequest req, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // A retry finds the account from an earlier attempt instead of creating another one.
        if (await accounts.FindByUsernameAsync(req.Username, ct) is not null)
            return await FindExistingAsync(issuer, req.Username, ct);

        var account = await accounts.CreateAsync(new NewIdentityProviderAccount(req.Username, req.Email, req.FirstName, req.LastName), ct);
        if (account is null)
            return ApplicationIdentityErrors.EmailInUse;

        // The mapping starts disabled and grants no progress access until provisioning finishes.
        var identity = ApplicationIdentity.Create(issuer, account.Subject);
        dbContext.ApplicationIdentities.Add(identity);
        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return identity;
    }

    /// <summary>
    /// Provisioning always creates an account and its mapping together. An account without a mapping was not
    /// created here, and linking it by username would bypass trusted provisioning.
    /// </summary>
    private async Task<ErrorOr<ApplicationIdentity>> FindExistingAsync(string issuer, string username, CancellationToken ct)
    {
        var account = await accounts.FindByUsernameAsync(username, ct);
        var existing = account is null ? null : await FindAsync(issuer, account.Subject, ct);
        return existing is null ? ApplicationIdentityErrors.AccountInUse : existing;
    }

    private Task<ApplicationIdentity?> FindAsync(string issuer, string subject, CancellationToken ct) =>
        dbContext.ApplicationIdentities.AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ByIssuerAndSubject(issuer, subject))
            .FirstOrDefaultAsync(ct);
}
