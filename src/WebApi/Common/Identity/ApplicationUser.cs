using Microsoft.AspNetCore.Identity;

namespace MedicalApp.Common.Identity;

/// <summary>
/// A sign-in account on the application's authorization server. The token subject is <c>Id.ToString("D")</c>.
/// Application access still comes from the <see cref="Domain.ApplicationIdentities.ApplicationIdentity"/> mapping.
/// </summary>
public class ApplicationUser : IdentityUser<Guid>
{
    /// <summary>
    /// Disabled accounts cannot sign in or exchange authorization codes. New accounts start disabled.
    /// </summary>
    public bool Enabled { get; set; }

    public string Subject => Id.ToString("D");
}
