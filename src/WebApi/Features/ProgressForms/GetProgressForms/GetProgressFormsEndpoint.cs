using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.GetProgressForms;

public class GetProgressFormsEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities)
    : Endpoint<GetProgressFormsRequest, GetProgressFormsResponse>
{
    public override void Configure()
    {
        Get("/");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("GetProgressForms").ProducesProblemDetails(400));
    }

    public override async Task HandleAsync(GetProgressFormsRequest req, CancellationToken ct)
    {
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var forms = await dbContext.ProgressForms.AsNoTracking()
            .WithSpecification(ProgressFormListSpec.OwnedPage(doctorId, req.Search, req.Page, req.PageSize))
            .ToListAsync(ct);
        var total = await dbContext.ProgressForms.WithSpecification(ProgressFormListSpec.Owned(doctorId, req.Search)).CountAsync(ct);

        await Send.OkAsync(new GetProgressFormsResponse(
            forms.Select(form => new GetProgressFormsResponse.FormDto(form.Id.Value, form.Title, form.LatestVersionNumber,
                form.DraftQuestions.Count, form.SourceFormId?.Value)).ToList(),
            total, req.Page, req.PageSize), ct);
    }
}
