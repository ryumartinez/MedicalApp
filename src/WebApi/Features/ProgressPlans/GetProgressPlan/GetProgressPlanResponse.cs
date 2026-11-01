using MedicalApp.Features.ProgressForms;

namespace MedicalApp.Features.ProgressPlans.GetProgressPlan;

/// <param name="Form">The assigned published version. Answers must use its question and option IDs.</param>
public record GetProgressPlanResponse(PlanDto Plan, GetProgressPlanResponse.FormVersionDto Form)
{
    public record FormVersionDto(Guid Id, Guid FormId, int VersionNumber, DateTimeOffset PublishedAt, string Title, string? Description,
        IReadOnlyList<QuestionDto> Questions);
}
