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

        builder.HasIndex(x => new { x.RepoId, x.Name }).IsUnique();
    }
}
