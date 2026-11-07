using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;
using MedicalApp.Common.Domain.ProgressPlans;

namespace MedicalApp.UnitTests.Features.Progress;

public sealed class ProgressEntryTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");
    private readonly ProgressFormVersion _version;
    private readonly ProgressEntry _entry;

    public ProgressEntryTests()
    {
        var doctor = DoctorId.From(Guid.CreateVersion7());
        _version = ProgressPlanTests.PublishedVersion(doctor,
            ProgressFormTests.Text("note", required: true),
            ProgressFormTests.Scale("mood", 0, 10, 2),
            ProgressFormTests.Choice("sleep", "Bad", "Good"),
            ProgressFormTests.Media("photo", QuestionType.Image));
        var schedule = ProgressSchedule.Create(ScheduleFrequency.Daily, "UTC", DateOnly.Parse("2026-11-01"), null).Value;
        var plan = ProgressPlan.Create(doctor, PatientId.From(Guid.CreateVersion7()), _version, schedule);
        _entry = ProgressEntry.Create(plan, schedule.PeriodStartingOn(DateOnly.Parse("2026-11-09"))!);
    }

    private ProgressQuestionId Q(string key) => _version.Questions.Single(q => q.Key == key).Id;

    [Fact]
    public void DraftsAcceptValidPartialAnswers()
    {
        var result = _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("mood"), Number = 4 }]);

        result.IsError.Should().BeFalse();
        _entry.Status.Should().Be(EntryStatus.Draft);
    }

    [Fact]
    public void RejectsInvalidSuppliedValues()
    {
        var sleep = _version.Questions.Single(q => q.Key == "sleep");
        var answers = new List<ProgressAnswer>
        {
            new() { QuestionId = ProgressQuestionId.From(Guid.CreateVersion7()), Text = "unknown" },
            new() { QuestionId = Q("mood"), Number = 3 },
            new() { QuestionId = Q("mood"), Number = 4 },
            new() { QuestionId = Q("sleep"), OptionId = ProgressQuestionOptionId.From(Guid.CreateVersion7()) },
            new() { QuestionId = Q("note"), Text = "   " },
            new() { QuestionId = Q("photo"), Text = "not a file" }
        };

        var result = _entry.SaveDraft(_version, answers);

        result.Errors.Select(e => e.Code).Should().BeEquivalentTo([
            "ProgressEntry.UnknownQuestion", "ProgressEntry.OutOfRange", "ProgressEntry.DuplicateAnswer",
            "ProgressEntry.UnknownOption", "ProgressEntry.EmptyText", "ProgressEntry.WrongType"]);
        _entry.Answers.Should().BeEmpty();
        sleep.Options.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(11)]
    [InlineData(-2)]
    [InlineData(4.5)]
    public void NumericAnswersFollowTheScale(double value)
    {
        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("mood"), Number = (decimal)value }])
            .FirstError.Code.Should().Be("ProgressEntry.OutOfRange");
    }

    [Fact]
    public void AnswersHoldExactlyOneValue()
    {
        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("note"), Text = "x", Number = 2 }])
            .FirstError.Code.Should().Be("ProgressEntry.OneValue");
        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("note") }])
            .FirstError.Code.Should().Be("ProgressEntry.OneValue");
        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("note"), Text = new string('a', 4001) }])
            .FirstError.Code.Should().Be("ProgressEntry.TextTooLong");
    }

    [Fact]
    public void SubmissionRequiresRequiredAnswersAndRecordsLateness()
    {
        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("mood"), Number = 4 }]);
        _entry.Submit(_version, Now).FirstError.Code.Should().Be("ProgressEntry.RequiredMissing");

        _entry.SaveDraft(_version, [new ProgressAnswer { QuestionId = Q("note"), Text = "Fine" },
            new ProgressAnswer { QuestionId = Q("photo"), MediaAssetId = MediaAssetId.From(Guid.CreateVersion7()) }]);
        _entry.Submit(_version, Now).IsError.Should().BeFalse();

        _entry.IsSubmitted.Should().BeTrue();
        _entry.SubmittedAt.Should().Be(Now);
        _entry.IsLate.Should().BeTrue();
        _entry.SaveDraft(_version, []).FirstError.Should().Be(ProgressEntryErrors.AlreadySubmitted);
        _entry.Submit(_version, Now).FirstError.Should().Be(ProgressEntryErrors.AlreadySubmitted);
        _entry.Answers.Should().HaveCount(2);
    }
}
