using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MedicalApp.Common.Domain.Base;

namespace MedicalApp.Common.Persistence;

public abstract class AuditableConfiguration<T> : IEntityTypeConfiguration<T>
    where T : Auditable
{
    public virtual void Configure(EntityTypeBuilder<T> builder)
    {
        builder.Property(e => e.CreatedBy)
            .HasMaxLength(Auditable.CreatedByMaxLength)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.UpdatedBy)
            .HasMaxLength(Auditable.UpdatedByMaxLength);

        PostConfigure(builder);
    }

    public abstract void PostConfigure(EntityTypeBuilder<T> builder);
}