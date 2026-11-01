using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace MigrationService.Initializers;

/// <summary>
/// Seeds the three first-release workflows for the development doctor and patient:
/// daily psychology tracking, weekly structured physical therapy, and weekly exercise videos.
/// Runs after <see cref="ProgressIdentityInitializer"/>. Existing forms are left unchanged, so repeated runs create nothing new.
/// </summary>
public class ProgressWorkflowInitializer(ApplicationDbContext dbContext, TimeProvider timeProvider)
{
    public const string TimeZone = "America/Santiago";

    public static readonly ProgressFormId DailyMoodFormId = ProgressFormId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000501"));
    public static readonly ProgressFormId KneeTherapyFormId = ProgressFormId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000502"));
    public static readonly ProgressFormId ExerciseVideoFormId = ProgressFormId.From(Guid.Parse("01967a92-2ff8-7a00-8000-000000000503"));

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var doctorId = ProgressIdentityInitializer.DoctorId;
        var patientId = ProgressIdentityInitializer.PatientId;
        if (!await dbContext.Doctors.AnyAsync(d => d.Id == doctorId, cancellationToken)
            || !await dbContext.Patients.AnyAsync(p => p.Id == patientId, cancellationToken))
            return;

        var strategy = dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            dbContext.ChangeTracker.Clear();
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            var now = timeProvider.GetUtcNow();
            var today = LocalToday(now);

            await SeedAsync(DailyMoodFormId, "Daily mood and energy", "Answer each evening.", DailyMood(), ScheduleFrequency.Daily,
                today.AddDays(-6), (plan, version) => SubmittedDays(plan, version, now), cancellationToken);
            await SeedAsync(KneeTherapyFormId, "Weekly knee therapy", "Answer after your last session of the week.", KneeTherapy(),
                ScheduleFrequency.Weekly, today.AddDays(-14), (_, _) => [], cancellationToken);
            await SeedAsync(ExerciseVideoFormId, "Weekly exercise videos", "Record one video for each exercise.", ExerciseVideos(),
                ScheduleFrequency.Weekly, today.AddDays(-7), (_, _) => [], cancellationToken);

            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }

    private async Task SeedAsync(ProgressFormId formId, string title, string description, IReadOnlyList<QuestionDefinition> questions,
        ScheduleFrequency frequency, DateOnly startDate, Func<ProgressPlan, ProgressFormVersion, IEnumerable<ProgressEntry>> entries,
        CancellationToken cancellationToken)
    {
        if (await dbContext.ProgressForms.AnyAsync(form => form.Id == formId, cancellationToken))
            return;

        var now = timeProvider.GetUtcNow();
        var form = ProgressForm.Create(ProgressIdentityInitializer.DoctorId, title, description, questions).Value;
        form.Id = formId;
        var version = form.Publish(now.AddDays(-30)).Value;
        var schedule = ProgressSchedule.Create(frequency, TimeZone, startDate, null).Value;
        var plan = ProgressPlan.Create(ProgressIdentityInitializer.DoctorId, ProgressIdentityInitializer.PatientId, version, schedule);

        dbContext.ProgressForms.Add(form);
        dbContext.ProgressFormVersions.Add(version);
        dbContext.ProgressPlans.Add(plan);
        dbContext.ProgressEntries.AddRange(entries(plan, version));
    }

    // Submitted entries for the last days, with one late submission and one missed day.
    private static IEnumerable<ProgressEntry> SubmittedDays(ProgressPlan plan, ProgressFormVersion version, DateTimeOffset now)
    {
        var mood = version.Questions.Single(q => q.Key == "mood");
        var energy = version.Questions.Single(q => q.Key == "energy");
        var sleep = version.Questions.Single(q => q.Key == "sleep");
        var notes = version.Questions.Single(q => q.Key == "notes");
        var day = 0;
        foreach (var period in plan.Schedule.PeriodsStartedBefore(now).Where(p => p.HasEnded(now)))
        {
            day++;
            if (day == 3)
                continue;

            var entry = ProgressEntry.Create(plan, period);
            entry.SaveDraft(version,
            [
                new ProgressAnswer { QuestionId = mood.Id, Number = 4 + day % 5 },
                new ProgressAnswer { QuestionId = energy.Id, Number = 3 + day % 4 },
                new ProgressAnswer { QuestionId = sleep.Id, OptionId = sleep.Options[day % sleep.Options.Count].Id },
                new ProgressAnswer { QuestionId = notes.Id, Text = $"Day {day} notes." }
            ]);
            var submittedAt = day == 2 ? period.EndUtc.AddHours(10) : period.EndUtc.AddHours(-3);
            entry.Submit(version, submittedAt);
            yield return entry;
        }
    }

    private DateOnly LocalToday(DateTimeOffset now) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(TimeZone)).DateTime);

    private static QuestionDefinition Question(string key, QuestionType type, string label, bool required = true) => new()
    {
        Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = key, Type = type, Label = label, Required = required
    };

    private static List<ChoiceOption> Options(params string[] labels) =>
        labels.Select(label => new ChoiceOption(ProgressQuestionOptionId.From(Guid.CreateVersion7()), label)).ToList();

    private static IReadOnlyList<QuestionDefinition> DailyMood() =>
    [
        Question("mood", QuestionType.NumericScale, "How was your mood today?") with { Min = 0, Max = 10, Step = 1, MinLabel = "Very low", MaxLabel = "Very good" },
        Question("energy", QuestionType.NumericScale, "How was your energy today?") with { Min = 0, Max = 10, Step = 1, MinLabel = "Exhausted", MaxLabel = "Energetic" },
        Question("sleep", QuestionType.SingleChoice, "How did you sleep last night?") with { Options = Options("Poorly", "Fairly", "Well") },
        Question("notes", QuestionType.Text, "Anything you want to share?", required: false) with { Instructions = "Optional." }
    ];

    private static IReadOnlyList<QuestionDefinition> KneeTherapy() =>
    [
        Question("pain", QuestionType.NumericScale, "Average knee pain this week") with { Min = 0, Max = 10, Step = 1, MinLabel = "No pain", MaxLabel = "Worst pain" },
        Question("sessions", QuestionType.NumericScale, "Exercise sessions completed") with { Min = 0, Max = 7, Step = 1 },
        Question("range", QuestionType.SingleChoice, "Can you fully bend your knee?") with { Options = Options("No", "Partly", "Yes") },
        Question("swelling-photo", QuestionType.Image, "Photo of the knee", required: false) with { AllowedContentTypes = MediaAsset.ImageContentTypes.ToList() },
        Question("comments", QuestionType.Text, "Comments for your therapist", required: false)
    ];

    private static IReadOnlyList<QuestionDefinition> ExerciseVideos() =>
    [
        Question("squat-video", QuestionType.Video, "Video: wall squats") with { AllowedContentTypes = MediaAsset.VideoContentTypes.ToList(), Instructions = "Film from the side." },
        Question("lunge-video", QuestionType.Video, "Video: lunges") with { AllowedContentTypes = MediaAsset.VideoContentTypes.ToList() },
        Question("bridge-video", QuestionType.Video, "Video: glute bridges", required: false) with { AllowedContentTypes = MediaAsset.VideoContentTypes.ToList() }
    ];
}
