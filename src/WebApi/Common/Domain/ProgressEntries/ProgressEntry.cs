using MedicalApp.Common.Domain.Base;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.Common.Domain.ProgressEntries;

[ValueObject<Guid>]
public readonly partial struct ProgressEntryId;

/// <summary>
/// A patient's answers for one plan period. Drafts can change; a submitted entry is immutable.
/// </summary>
public class ProgressEntry : Entity<ProgressEntryId>
{
    public ProgressPlanId PlanId { get; private set; }
    public PatientId PatientId { get; private set; }
    public ProgressFormVersionId FormVersionId { get; private set; }
    public DateOnly PeriodStartDate { get; private set; }
    public DateOnly PeriodEndDate { get; private set; }
    public DateTimeOffset PeriodStartUtc { get; private set; }
    public DateTimeOffset PeriodEndUtc { get; private set; }
    public EntryStatus Status { get; private set; }
    public DateTimeOffset? SubmittedAt { get; private set; }
    public bool IsLate { get; private set; }
    public IReadOnlyList<ProgressAnswer> Answers { get; private set; } = [];
    public uint Revision { get; private set; }

    private ProgressEntry() { }

    public static ProgressEntry Create(ProgressPlan plan, ReportingPeriod period) => new()
    {
        Id = ProgressEntryId.From(Guid.CreateVersion7()),
        PlanId = plan.Id,
        PatientId = plan.PatientId,
        FormVersionId = plan.FormVersionId,
        PeriodStartDate = period.StartDate,
        PeriodEndDate = period.EndDate,
        PeriodStartUtc = period.StartUtc,
        PeriodEndUtc = period.EndUtc,
        Status = EntryStatus.Draft
    };

    public ReportingPeriod Period => new(PeriodStartDate, PeriodEndDate, PeriodStartUtc, PeriodEndUtc);

    public bool IsSubmitted => Status == EntryStatus.Submitted;

    /// <summary>
    /// Replaces the draft answers. Supplied values must be valid; required answers can be missing.
    /// </summary>
    public ErrorOr<Success> SaveDraft(ProgressFormVersion version, IReadOnlyList<ProgressAnswer> answers)
    {
        if (IsSubmitted)
            return ProgressEntryErrors.AlreadySubmitted;
        if (version.Id != FormVersionId)
            throw new ArgumentException("The entry belongs to another version.", nameof(version));

        var errors = ValidateAnswers(version, answers);
        if (errors.Count != 0)
            return errors;
        Answers = answers;
        return Result.Success;
    }

    /// <summary>
    /// Submits the saved draft. Every required question needs an answer.
    /// Media answers are checked by the submission transaction before this call.
    /// </summary>
    public ErrorOr<Success> Submit(ProgressFormVersion version, DateTimeOffset now)
    {
        if (IsSubmitted)
            return ProgressEntryErrors.AlreadySubmitted;

        var errors = ValidateAnswers(version, Answers);
        var answered = Answers.Select(answer => answer.QuestionId).ToHashSet();
        errors.AddRange(version.Questions
            .Where(question => question.Required && !answered.Contains(question.Id))
            .Select(question => ProgressEntryErrors.RequiredMissing(question.Key)));
        if (errors.Count != 0)
            return errors;

        Status = EntryStatus.Submitted;
        SubmittedAt = now;
        IsLate = now >= PeriodEndUtc;
        return Result.Success;
    }

    public static List<Error> ValidateAnswers(ProgressFormVersion version, IReadOnlyList<ProgressAnswer> answers)
    {
        var errors = new List<Error>();
        var seen = new HashSet<ProgressQuestionId>();
        foreach (var (answer, index) in answers.Select((a, i) => (a, i)))
        {
            var field = $"Answers[{index}]";
            if (!seen.Add(answer.QuestionId))
            {
                errors.Add(ProgressEntryErrors.DuplicateAnswer(field));
                continue;
            }

            var question = version.FindQuestion(answer.QuestionId);
            if (question is null)
            {
                errors.Add(ProgressEntryErrors.UnknownQuestion(field));
                continue;
            }

            if (answer.Validate(question, field) is { } error)
                errors.Add(error);
        }

        return errors;
    }
}
