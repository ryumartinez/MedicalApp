using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.UnitTests.Features.Progress;

public sealed class ProgressFormTests
{
    private static readonly DoctorId Doctor = DoctorId.From(Guid.CreateVersion7());
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-01T12:00:00Z");

    internal static QuestionDefinition Text(string key, bool required = false) => new()
    {
        Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = key, Type = QuestionType.Text, Label = key, Required = required
    };

    internal static QuestionDefinition Scale(string key, int min = 1, int max = 10, int? step = null) => new()
    {
        Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = key, Type = QuestionType.NumericScale, Label = key,
        Min = min, Max = max, Step = step, MinLabel = "Low", MaxLabel = "High"
    };

    internal static QuestionDefinition Choice(string key, params string[] labels) => new()
    {
        Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = key, Type = QuestionType.SingleChoice, Label = key,
        Options = labels.Select(label => new ChoiceOption(ProgressQuestionOptionId.From(Guid.CreateVersion7()), label)).ToList()
    };

    internal static QuestionDefinition Media(string key, QuestionType type) => new()
    {
        Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = key, Type = type, Label = key,
        AllowedContentTypes = type == QuestionType.Image ? MediaAsset.ImageContentTypes.ToList() : MediaAsset.VideoContentTypes.ToList()
    };

    [Fact]
    public void PublishesAllFiveTypesWithNewVersionIdsAndStableKeys()
    {
        var questions = new[] { Text("note", true), Scale("mood", 0, 10, 2), Choice("sleep", "Bad", "Good"), Media("photo", QuestionType.Image), Media("video", QuestionType.Video) };
        var form = ProgressForm.Create(Doctor, "Daily", null, questions).Value;

        var first = form.Publish(Now).Value;
        var second = form.Publish(Now.AddDays(1)).Value;

        first.VersionNumber.Should().Be(1);
        second.VersionNumber.Should().Be(2);
        first.Questions.Select(q => q.Key).Should().Equal(questions.Select(q => q.Key));
        first.Questions.Select(q => q.Type).Should().Equal(questions.Select(q => q.Type));
        first.Questions.Select(q => q.Id).Should().NotIntersectWith(second.Questions.Select(q => q.Id));
        first.Questions.Select(q => q.Id).Should().NotIntersectWith(questions.Select(q => q.Id));
        first.Questions[1].Should().Match<QuestionDefinition>(q => q.Min == 0 && q.Max == 10 && q.Step == 2 && q.MinLabel == "Low");
        first.Questions[2].Options.Should().Equal(questions[2].Options);
        first.Questions[0].Required.Should().BeTrue();
    }

    [Fact]
    public void LaterDraftChangesDoNotAlterPublishedVersions()
    {
        var form = ProgressForm.Create(Doctor, "Daily", null, [Choice("sleep", "Bad", "Good")]).Value;
        var first = form.Publish(Now).Value;
        var option = first.Questions[0].Options[0];

        form.UpdateDraft("Changed", null, [Choice("sleep", "Terrible", "Fine", "Great")]).IsError.Should().BeFalse();
        form.Publish(Now).IsError.Should().BeFalse();

        first.Title.Should().Be("Daily");
        first.Questions[0].Options.Should().ContainSingle(o => o.Id == option.Id && o.Label == "Bad");
    }

    [Fact]
    public void DraftsCanBeIncompleteButPublicationRejectsThem()
    {
        var incomplete = new QuestionDefinition
        {
            Id = ProgressQuestionId.From(Guid.CreateVersion7()), Key = "mood", Type = QuestionType.NumericScale, Label = ""
        };
        var form = ProgressForm.Create(Doctor, "Daily", null, [incomplete, Choice("one", "Only")]).Value;

        var result = form.Publish(Now);

        result.IsError.Should().BeTrue();
        result.Errors.Select(e => e.Code).Should().Contain(["ProgressForm.LabelRequired", "ProgressForm.InvalidScale", "ProgressForm.TooFewOptions"]);
        form.LatestVersionNumber.Should().Be(0);
        ProgressForm.Create(Doctor, "Empty", null, []).Value.Publish(Now).FirstError.Should().Be(ProgressFormErrors.NoQuestions);
    }

    [Theory]
    [InlineData(1, 10, 4)]
    [InlineData(1, 10, 0)]
    public void RejectsStepsThatDoNotDivideTheScale(int min, int max, int step)
    {
        var form = ProgressForm.Create(Doctor, "Daily", null, [Scale("mood", min, max, step)]).Value;

        form.Publish(Now).Errors.Select(e => e.Code).Should().Contain("ProgressForm.InvalidStep");
    }

    [Fact]
    public void EnforcesRecordedLimits()
    {
        ProgressForm.Create(Doctor, new string('a', 201), null, []).FirstError.Code.Should().Be("ProgressForm.TooLong");
        ProgressForm.Create(Doctor, "Daily", new string('a', 2001), []).FirstError.Code.Should().Be("ProgressForm.TooLong");
        ProgressForm.Create(Doctor, "Daily", null, Enumerable.Range(0, 51).Select(i => Text($"q{i}")).ToList())
            .Errors.Should().Contain(ProgressFormErrors.TooManyQuestions);
        ProgressForm.Create(Doctor, "Daily", null, [Choice("c", Enumerable.Range(0, 21).Select(i => $"o{i}").ToArray())])
            .FirstError.Code.Should().Be("ProgressForm.TooManyOptions");
        ProgressForm.Create(Doctor, "Daily", null, [Text("note") with { Label = new string('a', 501) }])
            .FirstError.Code.Should().Be("ProgressForm.TooLong");
        ProgressForm.Create(Doctor, "Daily", null, [Choice("c", new string('a', 101), "b")])
            .FirstError.Code.Should().Be("ProgressForm.TooLong");
        ProgressForm.Create(Doctor, "Daily", null, [Text("Bad Key")]).FirstError.Code.Should().Be("ProgressForm.InvalidKey");
        ProgressForm.Create(Doctor, "Daily", null, [Text("same"), Text("same")]).Errors.Should().Contain(ProgressFormErrors.DuplicateKey);
    }

    [Fact]
    public void AttachmentQuestionsAcceptOnlyTheirMediaKind()
    {
        var mixed = Media("photo", QuestionType.Image) with { AllowedContentTypes = [MediaAsset.Png, MediaAsset.Mp4] };
        var unknown = Media("photo", QuestionType.Image) with { AllowedContentTypes = ["image/gif"] };

        ProgressForm.Create(Doctor, "Daily", null, [mixed]).Value.Publish(Now)
            .FirstError.Code.Should().Be("ProgressForm.UnsupportedContentType");
        ProgressForm.Create(Doctor, "Daily", null, [unknown]).FirstError.Code.Should().Be("ProgressForm.UnsupportedContentType");
        ProgressForm.Create(Doctor, "Daily", null, [Text("note") with { Min = 1 }]).FirstError.Code.Should().Be("ProgressForm.MembersDoNotMatchType");
    }

    [Fact]
    public void DuplicateIsIndependent()
    {
        var source = ProgressForm.Create(Doctor, "Daily", "Source", [Text("note")]).Value;
        source.Publish(Now);

        var copy = source.Duplicate();
        copy.UpdateDraft("Copy", null, [Text("other")]).IsError.Should().BeFalse();

        copy.Id.Should().NotBe(source.Id);
        copy.SourceFormId.Should().Be(source.Id);
        copy.LatestVersionNumber.Should().Be(0);
        source.Title.Should().Be("Daily");
        source.DraftQuestions.Single().Key.Should().Be("note");
    }
}
