using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using MedicalApp.Common.Domain.ProgressForms;

namespace MedicalApp.UnitTests.Features.Progress;

public sealed class MediaAssetTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-11-10T12:00:00Z");

    private static MediaAsset Asset(string contentType = MediaAsset.Png) => MediaAsset.Create(
        PatientId.From(Guid.CreateVersion7()), ProgressEntryId.From(Guid.CreateVersion7()), ProgressQuestionId.From(Guid.CreateVersion7()),
        contentType, Now);

    [Fact]
    public void MovesThroughPendingReadyAndAttached()
    {
        var asset = Asset();
        asset.State.Should().Be(MediaAssetState.Pending);
        asset.StagingKey.Should().StartWith("staging/");
        asset.UploadExpiresAt.Should().Be(Now.AddMinutes(15));

        asset.BeginPromotion("final/a/1");
        var wrongKey = () => asset.MarkReady("final/a/2", 10, "hash", Now);
        wrongKey.Should().Throw<InvalidOperationException>();
        asset.MarkReady("final/a/1", 10, "hash", Now);
        asset.State.Should().Be(MediaAssetState.Ready);
        asset.PromotionKey.Should().BeNull();

        asset.Attach(Now);
        asset.State.Should().Be(MediaAssetState.Attached);
        asset.IsExpiredBy(Now.AddYears(5)).Should().BeFalse();
        var expire = () => asset.Expire(Now);
        expire.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void PendingAndUnattachedAssetsExpireOnSchedule()
    {
        var pending = Asset();
        pending.CanFinalize(Now.AddHours(24)).Should().BeTrue();
        pending.IsExpiredBy(Now.AddMinutes(15).AddHours(24)).Should().BeTrue();
        pending.CanFinalize(Now.AddMinutes(15).AddHours(24)).Should().BeFalse();

        var ready = Asset();
        ready.BeginPromotion("final/b/1");
        ready.MarkReady("final/b/1", 10, "hash", Now);
        ready.IsExpiredBy(Now.AddDays(7).AddSeconds(-1)).Should().BeFalse();
        ready.IsExpiredBy(Now.AddDays(7)).Should().BeTrue();
        ready.Expire(Now.AddDays(7));
        var attach = () => ready.Attach(Now);
        attach.Should().Throw<InvalidOperationException>();
        ready.ObjectKeys.Should().BeEquivalentTo([ready.StagingKey, "final/b/1"]);
    }

    [Fact]
    public void EnforcesSizeLimits()
    {
        var image = Asset();
        image.BeginPromotion("k");
        var tooBig = () => image.MarkReady("k", MediaAsset.MaxImageBytes + 1, "hash", Now);
        tooBig.Should().Throw<ArgumentOutOfRangeException>();
        Asset(MediaAsset.Mp4).MaxBytes.Should().Be(200L * 1024 * 1024);
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, MediaAsset.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, MediaAsset.Png)]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 }, MediaAsset.WebP)]
    [InlineData(new byte[] { 0, 0, 0, 0x18, 0x66, 0x74, 0x79, 0x70, 0x69, 0x73, 0x6F, 0x6D }, MediaAsset.Mp4)]
    [InlineData(new byte[] { 0x47, 0x49, 0x46, 0x38 }, null)]
    [InlineData(new byte[] { 0x3C, 0x68, 0x74, 0x6D, 0x6C }, null)]
    public void DetectsFormatFromSignature(byte[] header, string? expected)
    {
        MediaAsset.DetectContentType(header).Should().Be(expected);
    }

    [Fact]
    public void RejectsUnsupportedContentTypes()
    {
        var create = () => Asset("image/gif");
        create.Should().Throw<ArgumentException>();
    }
}
