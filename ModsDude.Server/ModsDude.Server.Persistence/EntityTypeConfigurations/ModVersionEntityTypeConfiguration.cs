using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Persistence.Changes;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModVersionEntityTypeConfiguration : IEntityTypeConfiguration<ModVersion>
{
    public void Configure(EntityTypeBuilder<ModVersion> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.ModId, x.Id });

        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(x => x.Attributes);

        builder.OwnsMany(x => x.Images, image =>
        {
            image.Property(x => x.Kind).HasConversion<string>();
            image.Property(x => x.Rendition).HasConversion<string>();
        });

        // Bounded to what ModFileName accepts, so the column cannot hold a name the validation
        // would have refused had it been a byte longer.
        builder.Property(x => x.FileName).HasMaxLength(255);

        builder.HasIndex(x => new { x.RepoId, x.ModId, x.SequenceNumber }).IsUnique();

        builder.ConfigureDeletionSchedule(x => x.DeletionScheduledFor, x => x.DeletionReason);

        // Stamped by the database on every insert and update - see RepoChangeCounter. Unique within a
        // repo, which is what lets the change feed page on it alone.
        builder.Property<long>(ModChanges.SequenceColumn).ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(nameof(ModVersion.RepoId), ModChanges.SequenceColumn).IsUnique();
    }
}
