using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.UpdateProgressFormDraft;

public class UpdateProgressFormDraftEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities)
    : Endpoint<UpdateProgressFormDraftRequest, UpdateProgressFormDraftResponse>
{
    public override void Configure()
    {
        Put("/{id}/draft");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("UpdateProgressFormDraft")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(UpdateProgressFormDraftRequest req, CancellationToken ct)
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

        var result = form.UpdateDraft(req.Title, req.Description, QuestionDto.ToDefinitions(req.Questions));
        if (result.IsError)
        {
            await HttpContext.SendErrorsAsync(result.Errors, ct);
            return;
        }

        try
        {
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another update committed between the read and this write.
            await HttpContext.SendErrorsAsync([ProgressFormErrors.Conflict], ct);
            return;
        }

        await Send.OkAsync(new UpdateProgressFormDraftResponse(form.Id.Value, form.Revision), ct);
    }
}
