using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Common.Services;

/// <summary>
/// Supplies the audit identifier for the current request.
/// Mapped callers use their application identity ID. Other authenticated callers use their raw token issuer and subject.
/// </summary>
public class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    public string? UserId
    {
        get
        {
            var httpContext = httpContextAccessor.HttpContext;
            if (httpContext is null)
                return null;

            if (httpContext.Items.TryGetValue(CurrentIdentityService.AuditIdentityKey, out var mapped) && mapped is string id)
                return id;

            var issuer = httpContext.User.FindFirstValue("iss");
            var subject = httpContext.User.FindFirstValue("sub");
            if (httpContext.User.Identity?.IsAuthenticated != true || string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(subject))
                return null;

            return FromIssuerAndSubject(issuer, subject);
        }
    }

    public static string FromIssuerAndSubject(string issuer, string subject)
    {
        var value = $"{subject}@{issuer}";
        if (value.Length <= Auditable.CreatedByMaxLength)
            return value;

        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
        return $"sha256:{hash}";
    }
}
