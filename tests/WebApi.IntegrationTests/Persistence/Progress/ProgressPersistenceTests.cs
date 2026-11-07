using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Persistence.Progress;

public sealed class ProgressPersistenceTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");

    private static QuestionDefinition Question(string key, QuestionType type) => type switch
    {
        QuestionType.NumericScale => new QuestionDefinition { Id = Qid(), Key = key, Type = type, Label = key, Min = 0, Max = 10, Step = 1, MinLabel = "Low", MaxLabel = "High" },
        QuestionType.SingleChoice => new QuestionDefinition { Id = Qid(), Key = key, Type = type, Label = key,
            Options = [new ChoiceOption(ProgressQuestionOptionId.From(Guid.CreateVersion7()), "Yes"), new ChoiceOption(ProgressQuestionOptionId.From(Guid.CreateVersion7()), "No")] },
        QuestionType.Image => new QuestionDefinition { Id = Qid(), Key = key, Type = type, Label = key, AllowedContentTypes = [MediaAsset.Png] },
        QuestionType.Video => new QuestionDefinition { Id = Qid(), Key = key, Type = type, Label = key, AllowedContentTypes = [MediaAsset.Mp4] },
        _ => new QuestionDefinition { Id = Qid(), Key = key, Type = type, Label = key, Required = true, Instructions = "Describe your day" }
    };

    private static ProgressQuestionId Qid() => ProgressQuestionId.From(Guid.CreateVersion7());

    private async Task<(Doctor Doctor, Patient Patient, ProgressForm Form, ProgressFormVersion Version)> SeedFormAsync()
    {
        var doctor = Doctor.Create("Dr. Smith", "Psychology");
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        await AddAsync(doctor);
        await AddAsync(patient);
        var form = ProgressForm.Create(doctor.Id, "Daily", "All types",
            [Question("note", QuestionType.Text), Question("mood", QuestionType.NumericScale), Question("sleep", QuestionType.SingleChoice),
             Question("photo", QuestionType.Image), Question("video", QuestionType.Video)]).Value;
        var version = form.Publish(Now).Value;
        await AddAsync(form);
        await AddAsync(version);
        return (doctor, patient, form, version);
    }

    [Fact]
    public async Task RoundTripsEveryProgressRecordWithTypedIdsAndDocuments()
    {
        var (doctor, patient, form, version) = await SeedFormAsync();
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Weekly, "America/Santiago", DateOnly.Parse("2026-11-04"), DateOnly.Parse("2026-12-31")).Value;
        var plan = ProgressPlan.Create(doctor.Id, patient.Id, version, schedule);
        await AddAsync(plan);
        var reviewer = Doctor.Create("Dr. Jones", "Physiotherapy");
        await AddAsync(reviewer);
        await AddAsync(ProgressPlanGrant.Create(plan.Id, reviewer.Id));
        var entry = ProgressEntry.Create(plan, schedule.PeriodStartingOn(DateOnly.Parse("2026-11-04"))!);
        var asset = MediaAsset.Create(patient.Id, entry.Id, version.Questions[3].Id, MediaAsset.Png, Now);
        entry.SaveDraft(version, [
            new ProgressAnswer { QuestionId = version.Questions[0].Id, Text = "Good" },
            new ProgressAnswer { QuestionId = version.Questions[1].Id, Number = 7 },
            new ProgressAnswer { QuestionId = version.Questions[2].Id, OptionId = version.Questions[2].Options[1].Id },
            new ProgressAnswer { QuestionId = version.Questions[3].Id, MediaAssetId = asset.Id }]).IsError.Should().BeFalse();
        await AddAsync(entry);
        await AddAsync(asset);

        var savedVersion = await GetQueryable<ProgressFormVersion>().SingleAsync(CancellationToken);
        savedVersion.Questions.Should().BeEquivalentTo(version.Questions);
        (await GetQueryable<ProgressForm>().SingleAsync(CancellationToken)).DraftQuestions.Should().BeEquivalentTo(form.DraftQuestions);
        var savedPlan = await GetQueryable<ProgressPlan>().SingleAsync(CancellationToken);
        savedPlan.Schedule.Should().Be(schedule);
        savedPlan.Revision.Should().NotBe(0u);
        var savedEntry = await GetQueryable<ProgressEntry>().SingleAsync(CancellationToken);
        savedEntry.Answers.Should().BeEquivalentTo(entry.Answers);
        savedEntry.Period.Should().Be(entry.Period);
        (await GetQueryable<MediaAsset>().SingleAsync(CancellationToken)).StagingKey.Should().Be(asset.StagingKey);
        (await GetQueryable<ProgressPlanGrant>().SingleAsync(CancellationToken)).DoctorId.Should().Be(reviewer.Id);
    }

    [Fact]
    public async Task PublishedVersionNumbersAreUniquePerForm()
    {
        var (_, _, form, _) = await SeedFormAsync();
        using var scope = CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var copy = await db.ProgressForms.SingleAsync(f => f.Id == form.Id, CancellationToken);

        // Simulate a second writer that computed the same number.
        var duplicate = copy.Publish(Now).Value;
        db.Entry(duplicate).Property(v => v.VersionNumber).CurrentValue = 1;
        db.ProgressFormVersions.Add(duplicate);
        db.Entry(copy).State = EntityState.Unchanged;

        var save = () => db.SaveChangesAsync(CancellationToken);
        await save.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task ConcurrentFormUpdatesConflict()
    {
        var (_, _, form, _) = await SeedFormAsync();
        using var firstScope = CreateScope();
        using var secondScope = CreateScope();
        var first = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var second = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var a = await first.ProgressForms.SingleAsync(f => f.Id == form.Id, CancellationToken);
        var b = await second.ProgressForms.SingleAsync(f => f.Id == form.Id, CancellationToken);

        a.UpdateDraft("First", null, a.DraftQuestions);
        await first.SaveChangesAsync(CancellationToken);
        b.UpdateDraft("Second", null, b.DraftQuestions);
        var save = () => second.SaveChangesAsync(CancellationToken);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
        (await GetQueryable<ProgressForm>().SingleAsync(CancellationToken)).Title.Should().Be("First");
    }

    [Fact]
    public async Task EachPlanPeriodHasOneEntry()
    {
        var (doctor, patient, _, version) = await SeedFormAsync();
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse("2026-11-01"), null).Value;
        var plan = ProgressPlan.Create(doctor.Id, patient.Id, version, schedule);
        await AddAsync(plan);
        var period = schedule.PeriodStartingOn(DateOnly.Parse("2026-11-02"))!;
        await AddAsync(ProgressEntry.Create(plan, period));

        var duplicate = () => AddAsync(ProgressEntry.Create(plan, period));

        await duplicate.Should().ThrowAsync<UniqueConstraintException>();
    }

    [Fact]
    public async Task PlansReferenceExistingPublishedVersions()
    {
        var (doctor, patient, form, version) = await SeedFormAsync();
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse("2026-11-01"), null).Value;
        var plan = ProgressPlan.Create(doctor.Id, patient.Id, version, schedule);
        typeof(ProgressPlan).GetProperty(nameof(ProgressPlan.FormVersionId))!.SetValue(plan, ProgressFormVersionId.From(Guid.CreateVersion7()));

        var save = () => AddAsync(plan);

        await save.Should().ThrowAsync<ReferenceConstraintException>();
        form.Id.Should().Be(version.FormId);
    }
}
