using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MedicalApp.Common.Domain.Teams;
using MedicalApp.Common.Persistence.Heroes;

namespace MedicalApp.Common.Persistence.Teams;

public class TeamConfiguration : AuditableConfiguration<Team>
{
    public override void PostConfigure(EntityTypeBuilder<Team> builder)
    {
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .HasMaxLength(Team.NameMaxLength)
            .IsRequired();

        builder.HasMany(t => t.Missions)
            .WithOne()
            .IsRequired();

        builder.HasMany(t => t.Heroes)
            .WithOne()
            .HasForeignKey(h => h.TeamId)
            .IsRequired(false);
    }
}