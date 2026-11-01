using MedicalApp.Common.Authorization;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressPlans.GetPatientProgressPlans;

public class GetPatientProgressPlansEndpoint(ICurrentIdentityService identities, ProgressAuthorizationService authorization)
    : Endpoint<GetPatientProgressPlansRequest, GetPatientProgressPlansResponse>
{
    public override void Configure()
    {
        Get("/");
        Group<ProgressPlansGroup>();
        Description(x => x.WithName("GetPatientProgressPlans").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetPatientProgressPlansRequest req, CancellationToken ct)
    {
        var identity = (await identities.GetAsync(ct))!;
        var plans = authorization.PlansVisibleTo(identity).AsNoTracking();

        if (req.PatientId is { } requested)
        {
            var patientId = PatientId.From(requested);
            plans = plans.Where(plan => plan.PatientId == patientId);
        }
        else if (identity.PatientId is { } own)
        {
            plans = plans.Where(plan => plan.PatientId == own);
        }
        else
        {
            AddError(r => r.PatientId, "PatientId is required for doctors.");
            await Send.ErrorsAsync(cancellation: ct);
            return;
        }

        if (!req.IncludeEnded)
            plans = plans.Where(plan => plan.EndedAt == null);

        var items = await plans.OrderBy(plan => plan.StartDate).ThenBy(plan => plan.Id).ToListAsync(ct);
        await Send.OkAsync(new GetPatientProgressPlansResponse(items.Select(PlanDto.From).ToList()), ct);
    }
}
