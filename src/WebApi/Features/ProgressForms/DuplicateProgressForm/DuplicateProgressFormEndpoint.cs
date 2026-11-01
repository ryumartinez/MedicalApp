using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.DuplicateProgressForm;

public class DuplicateProgressFormEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities)
    : EndpointWithoutRequest<DuplicateProgressFormResponse>
{
    public override void Configure()
    {
        Post("/{id:guid}/duplicate");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("DuplicateProgressForm").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var source = await dbContext.ProgressForms.AsNoTracking()
            .WithSpecification(ProgressFormSpec.OwnedById(ProgressFormId.From(Route<Guid>("id")), doctorId))
            .SingleOrDefaultAsync(ct);
        if (source is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var copy = source.Duplicate();
        dbContext.ProgressForms.Add(copy);
        await dbContext.SaveChangesAsync(ct);
        await Send.CreatedAtAsync("GetProgressForm", new { id = copy.Id.Value },
            new DuplicateProgressFormResponse(copy.Id.Value, source.Id.Value, copy.Revision), cancellation: ct);
    }
}
