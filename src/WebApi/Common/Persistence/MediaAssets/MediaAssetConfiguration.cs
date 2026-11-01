using MedicalApp.Common.Domain.MediaAssets;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.Domain.ProgressEntries;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MedicalApp.Common.Persistence.MediaAssets;

public class MediaAssetConfiguration : AuditableConfiguration<MediaAsset>
{
    public const int KeyMaxLength = 200;
    public const int ContentTypeMaxLength = 100;

    public override void PostConfigure(EntityTypeBuilder<MediaAsset> builder)
    {
        builder.HasKey(asset => asset.Id);
        builder.Property(asset => asset.ContentType).HasMaxLength(ContentTypeMaxLength).IsRequired();
        builder.Property(asset => asset.StagingKey).HasMaxLength(KeyMaxLength).IsRequired();
        builder.Property(asset => asset.PromotionKey).HasMaxLength(KeyMaxLength);
        builder.Property(asset => asset.FinalKey).HasMaxLength(KeyMaxLength);
        builder.Property(asset => asset.Sha256).HasMaxLength(64);
        builder.Property(asset => asset.Revision).IsXminRevision();
        builder.Ignore(asset => asset.ObjectKeys);
        builder.HasOne<Patient>().WithMany().HasForeignKey(asset => asset.PatientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ProgressEntry>().WithMany().HasForeignKey(asset => asset.EntryId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(asset => asset.StagingKey).IsUnique();
        builder.HasIndex(asset => asset.EntryId);
        builder.HasIndex(asset => new { asset.State, asset.UploadExpiresAt });
        builder.ToTable(table =>
        {
            table.HasCheckConstraint("CK_MediaAsset_Ready",
                "\"State\" IN (0, 3) OR (\"FinalKey\" IS NOT NULL AND \"Size\" IS NOT NULL AND \"Sha256\" IS NOT NULL AND \"ReadyAt\" IS NOT NULL)");
            table.HasCheckConstraint("CK_MediaAsset_Attached", "(\"State\" = 2) = (\"AttachedAt\" IS NOT NULL)");
        });
    }
}
