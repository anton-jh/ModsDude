using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Storage;
using ModsDude.Server.Persistence.Changes;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModVersionEntityTypeConfiguration : IEntityTypeConfiguration<ModVersion>
{
    public void Configure(EntityTypeBuilder<ModVersion> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.ModId, x.Id }).HasName(ModVersionConstraints.PrimaryKey);

        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId).OnDelete(DeleteBehavior.Restrict);

        builder.OwnsMany(x => x.Attributes);

        builder.OwnsMany(x => x.Images, image =>
        {
            image.Property(x => x.Kind).HasConversion<string>();
            image.Property(x => x.Rendition).HasConversion<string>();

            // Asked "which of these hashes does anything use?" before every import.
            image.HasIndex(x => x.Hash);
        });

        // Bounded to what ModFileName accepts, so the column cannot hold a name the validation
        // would have refused had it been a byte longer.
        builder.Property(x => x.FileName).HasMaxLength(255);

        builder.Property(x => x.ContentHash).HasMaxLength(Sha256Hex.Length);
        builder.ToTable(x => x.HasCheckConstraint("CK_ModVersions_ContentHashIsSha256Hex", "\"ContentHash\" ~ '^[0-9a-f]{64}$'"));

        builder.HasIndex(x => new { x.RepoId, x.ModId, x.SequenceNumber }).IsUnique().HasDatabaseName(ModVersionConstraints.SequenceIndex);

        builder.ConfigureDeletionSchedule(x => x.DeletionScheduledFor, x => x.DeletionReason);

        // Stamped by the database on every insert and update - see RepoChangeCounter. Unique within a
        // repo, which is what lets the change feed page on it alone.
        builder.Property<long>(ModChanges.SequenceColumn).ValueGeneratedOnAddOrUpdate();
        builder.HasIndex(nameof(ModVersion.RepoId), ModChanges.SequenceColumn).IsUnique();
    }
}


/// <summary>The constraints a concurrent registration or move can run into, named so endpoints can tell them apart.</summary>
public static class ModVersionConstraints
{
    /// <summary>Violated by a second registration of one version.</summary>
    public const string PrimaryKey = "PK_ModVersions";

    /// <summary>Violated by two versions of one mod claiming one place in its order.</summary>
    public const string SequenceIndex = "IX_ModVersions_RepoId_ModId_SequenceNumber";
}
