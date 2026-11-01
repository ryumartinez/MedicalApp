using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Interfaces;

namespace MedicalApp.Features.ProgressForms.GetProgressForm;

public class GetProgressFormEndpoint(ApplicationDbContext dbContext, ICurrentIdentityService identities)
    : Endpoint<GetProgressFormRequest, GetProgressFormResponse>
{
    public override void Configure()
    {
        Get("/{id}");
        Group<ProgressFormsGroup>();
        Description(x => x.WithName("GetProgressForm").ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(GetProgressFormRequest req, CancellationToken ct)
    {
        var doctorId = await identities.GetDoctorIdAsync(ct);
        var form = await dbContext.ProgressForms.AsNoTracking()
            .WithSpecification(ProgressFormSpec.OwnedById(ProgressFormId.From(req.Id), doctorId))
            .SingleOrDefaultAsync(ct);
        if (form is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        var versions = await dbContext.ProgressFormVersions.AsNoTracking()
            .WithSpecification(ProgressFormVersionSpec.ForForm(form.Id))
            .ToListAsync(ct);

        await Send.OkAsync(new GetProgressFormResponse(
            form.Id.Value, form.Title, form.Description, form.Revision, form.LatestVersionNumber, form.SourceFormId?.Value,
            form.DraftQuestions.Select(QuestionDto.From).ToList(),
            versions.Select(version => new GetProgressFormResponse.VersionDto(version.Id.Value, version.VersionNumber,
                version.PublishedAt, version.Title, version.Description, version.Questions.Select(QuestionDto.From).ToList())).ToList()), ct);
    }
}
