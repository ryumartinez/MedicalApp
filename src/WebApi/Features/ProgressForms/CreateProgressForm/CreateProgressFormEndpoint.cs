using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.FastEndpoints;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.CreateProgressForm;

public class CreateProgressFormEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities)
    : Endpoint<CreateProgressFormRequest, CreateProgressFormResponse>
{
    public override void Configure()
    {
        Post("/");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("CreateProgressForm").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(CreateProgressFormRequest req, CancellationToken ct)
    {
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var form = ProgressForm.Create(doctorId, req.Title, req.Description, QuestionDto.ToDefinitions(req.Questions));
        if (form.IsError)
        {
            await HttpContext.SendErrorsAsync(form.Errors, ct);
            return;
        }

        dbContext.ProgressForms.Add(form.Value);
        await dbContext.SaveChangesAsync(ct);
        await Send.CreatedAtAsync("GetProgressForm", new { id = form.Value.Id.Value },
            new CreateProgressFormResponse(form.Value.Id.Value, form.Value.Revision), cancellation: ct);
    }
}
