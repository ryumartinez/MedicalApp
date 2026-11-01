using MedicalApp.Common.Authentication;
using MedicalApp.Common.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace MedicalApp.Common.Authorization;

/// <summary>
/// Endpoint-level policies. Each requires the API client role, and doctor and patient policies also require
/// an enabled role-specific mapping in current application state.
/// </summary>
public static class ProgressPolicies
{
    public const string Doctor = "progress-doctor";
    public const string Patient = "progress-patient";
    public const string DoctorOrPatient = "progress-doctor-or-patient";
    public const string Administrator = "progress-administrator";

    public static void Configure(AuthorizationOptions options)
    {
        options.AddPolicy(Doctor, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new MappedRoleRequirement(ApiRoles.Doctor)));
        options.AddPolicy(Patient, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new MappedRoleRequirement(ApiRoles.Patient)));
        options.AddPolicy(DoctorOrPatient, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new MappedRoleRequirement(ApiRoles.Doctor, ApiRoles.Patient)));
        options.AddPolicy(Administrator, policy => policy
            .RequireAuthenticatedUser()
            .RequireClaim(ApiRoles.ClaimType, ApiRoles.Administrator));
    }
}

/// <summary>
/// Satisfied when the caller holds any of the roles together with the matching enabled mapping.
/// </summary>
public sealed class MappedRoleRequirement(params string[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

// Resolves the identity service only when a mapped-role requirement is evaluated,
// so unrelated authorization checks do not need the database.
public sealed class MappedRoleHandler(IServiceProvider serviceProvider)
    : AuthorizationHandler<MappedRoleRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, MappedRoleRequirement requirement)
    {
        var identity = await serviceProvider.GetRequiredService<ICurrentIdentityService>().GetAsync();
        if (identity is null)
            return;

        var allowed = requirement.Roles.Any(role => role switch
        {
            ApiRoles.Doctor => identity.DoctorId is not null,
            ApiRoles.Patient => identity.PatientId is not null,
            _ => false
        });
        if (allowed)
            context.Succeed(requirement);
    }
}
