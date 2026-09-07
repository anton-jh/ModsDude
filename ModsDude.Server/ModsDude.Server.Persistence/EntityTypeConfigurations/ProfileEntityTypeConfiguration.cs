using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ProfileEntityTypeConfiguration : IEntityTypeConfiguration<Profile>
{
    public void Configure(EntityTypeBuilder<Profile> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.Id });

        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId);

        builder.Property(x => x.Name);
        builder.Property(x => x.Created);

        // A scalar, and there is deliberately no navigation to the revisions themselves - see
        // Profile for why loading a profile must not be able to drag its history in with it.
        builder.Property(x => x.HeadRevision);

        // Filtered on ArchivedAt: an archived profile gives up its name, so the name is free again
        // immediately and several archived profiles may share one. Restoring is where a clash has to
        // be resolved, by renaming - see IArchivable.
        builder.HasIndex(x => new { x.RepoId, x.Name })
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL");
    }
}
