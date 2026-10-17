using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Common.Persistence.Heroes;

namespace MedicalApp.Common.Persistence.Teams;

public class MissionConfiguration : AuditableConfiguration<Mission>
{
    public override void PostConfigure(EntityTypeBuilder<Mission> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Description)
            .HasMaxLength(Mission.DescriptionMaxLength)
            .IsRequired();
    }
}