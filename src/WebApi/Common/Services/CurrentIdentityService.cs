using Ardalis.Specification.EntityFrameworkCore;
using System.Security.Claims;
using MedicalApp.Common.Authentication;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Common.Services;

public sealed class CurrentIdentityService(IHttpContextAccessor httpContextAccessor, ApplicationDbContext dbContext)
    : ICurrentIdentityService
{
    internal const string AuditIdentityKey = "MedicalApp.AuditIdentity";

    private CurrentIdentity? _identity;
    private bool _resolved;

    public async Task<CurrentIdentity?> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_resolved)
            return _identity;

        var httpContext = httpContextAccessor.HttpContext;
        var principal = httpContext?.User;
        var issuer = principal?.FindFirstValue("iss");
        var subject = principal?.FindFirstValue("sub");
        if (principal?.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
        {
            _resolved = true;
            return null;
        }

        var roles = principal.FindAll(ApiRoles.ClaimType).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);
        var mapping = await dbContext.ApplicationIdentities
            .AsNoTracking()
            .WithSpecification(ApplicationIdentitySpec.ByIssuerAndSubject(issuer, subject))
            .FirstOrDefaultAsync(cancellationToken);

        var enabled = mapping is { Enabled: true };
        _identity = new CurrentIdentity(
            issuer,
            subject,
            roles,
            mapping?.Id,
            enabled && roles.Contains(ApiRoles.Doctor) ? mapping!.DoctorId : null,
            enabled && roles.Contains(ApiRoles.Patient) ? mapping!.PatientId : null);
        _resolved = true;

        if (mapping is not null)
            httpContext!.Items[AuditIdentityKey] = mapping.Id.Value.ToString();

        return _identity;
    }
}
