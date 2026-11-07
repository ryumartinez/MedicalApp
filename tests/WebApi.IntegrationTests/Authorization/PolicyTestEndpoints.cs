using FastEndpoints;
using MedicalApp.Common.Authorization;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.IntegrationTests.Authorization;

/// <summary>
/// Test-only endpoints that report the caller's resolved records behind each mapped-role policy.
/// </summary>
public sealed class DoctorPolicyTestEndpoint(ICurrentIdentityService identities) : EndpointWithoutRequest<Guid>
{
    public override void Configure()
    {
        Get("/test/doctor-policy");
        Policies(ProgressPolicies.Doctor);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync((await identities.GetAsync(ct))!.DoctorId!.Value.Value, ct);
}

public sealed class PatientPolicyTestEndpoint(ICurrentIdentityService identities) : EndpointWithoutRequest<Guid>
{
    public override void Configure()
    {
        Get("/test/patient-policy");
        Policies(ProgressPolicies.Patient);
    }

    public override async Task HandleAsync(CancellationToken ct) =>
        await Send.OkAsync((await identities.GetAsync(ct))!.PatientId!.Value.Value, ct);
}
