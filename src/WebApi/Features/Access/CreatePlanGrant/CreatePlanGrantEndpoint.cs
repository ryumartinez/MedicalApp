using Ardalis.Specification.EntityFrameworkCore;
using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.FastEndpoints;

namespace MedicalApp.Features.Access.CreatePlanGrant;

public class CreatePlanGrantEndpoint(ApplicationDbContext dbContext) : Endpoint<CreatePlanGrantRequest, CreatePlanGrantResponse>
{
    public override void Configure()
    {
        Post("/plan-grants");
        Group<AccessGroup>();
        Description(x => x.WithName("CreatePlanGrant").ProducesProblemDetails(400).ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CreatePlanGrantRequest req, CancellationToken ct)
    {
        var planId = ProgressPlanId.From(req.PlanId);
        var doctorId = DoctorId.From(req.DoctorId);
        var plan = await dbContext.ProgressPlans.AsNoTracking().WithSpecification(ProgressPlanSpec.ById(planId)).SingleOrDefaultAsync(ct);
        if (plan is null)
        {
            await HttpContext.SendErrorsAsync([ProgressPlanErrors.NotFound], ct);
            return;
        }
        if (!await dbContext.Doctors.WithSpecification(DoctorSpec.ById(doctorId)).AnyAsync(ct))
        {
            await HttpContext.SendErrorsAsync([DoctorErrors.NotFound], ct);
            return;
        }
        if (plan.DoctorId == doctorId)
        {
            await HttpContext.SendErrorsAsync([AccessErrors.OwnerGrant], ct);
            return;
        }

        var grant = await FindAsync(planId, doctorId, ct);
        if (grant is null)
        {
            grant = ProgressPlanGrant.Create(planId, doctorId);
            dbContext.ProgressPlanGrants.Add(grant);
            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintException)
            {
                dbContext.ChangeTracker.Clear();
                grant = (await FindAsync(planId, doctorId, ct))!;
            }
        }

        if (!grant.IsActive)
        {
            grant.Reactivate();
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.OkAsync(new CreatePlanGrantResponse(grant.Id.Value, req.PlanId, req.DoctorId, grant.IsActive), ct);
    }

    private Task<ProgressPlanGrant?> FindAsync(ProgressPlanId planId, DoctorId doctorId, CancellationToken ct) =>
        dbContext.ProgressPlanGrants.SingleOrDefaultAsync(grant => grant.PlanId == planId && grant.DoctorId == doctorId, ct);
}
