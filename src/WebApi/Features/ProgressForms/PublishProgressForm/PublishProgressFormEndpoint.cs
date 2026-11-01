using Ardalis.Specification.EntityFrameworkCore;
using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.PublishProgressForm;

public class PublishProgressFormEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities, TimeProvider timeProvider)
    : Endpoint<PublishProgressFormRequest, PublishProgressFormResponse>
{
    public override void Configure()
    {
        Post("/{id}/publish");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("PublishProgressForm")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(PublishProgressFormRequest req, CancellationToken ct)
    {
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var form = await dbContext.ProgressForms
            .WithSpecification(ProgressFormSpec.OwnedById(ProgressFormId.From(req.Id), doctorId))
            .SingleOrDefaultAsync(ct);
        if (form is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (form.Revision != req.Revision)
        {
            await HttpContext.SendErrorsAsync([ProgressFormErrors.Conflict], ct);
            return;
        }

        var version = form.Publish(timeProvider.GetUtcNow());
        if (version.IsError)
        {
            await HttpContext.SendErrorsAsync(version.Errors, ct);
            return;
        }

        // The form's revision and the unique version number both stop a concurrent publication.
        dbContext.ProgressFormVersions.Add(version.Value);
        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (Exception exception) when (exception is DbUpdateConcurrencyException or UniqueConstraintException)
        {
            await HttpContext.SendErrorsAsync([ProgressFormErrors.Conflict], ct);
            return;
        }

        await Send.OkAsync(new PublishProgressFormResponse(form.Id.Value, version.Value.Id.Value, version.Value.VersionNumber,
            version.Value.PublishedAt, form.Revision), ct);
    }
}
