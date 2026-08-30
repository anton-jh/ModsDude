using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

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

        builder.HasIndex(x => new { x.RepoId, x.ModId, x.SequenceNumber }).IsUnique();

        // Backs the delta form of the mod list, which orders by Updated inside a repo and resumes
        // from a timestamp. Without it the endpoint that exists to make repeated syncs cheap sorts
        // every version in the repo on every page.
        builder.HasIndex(x => new { x.RepoId, x.Updated, x.ModId, x.Id });
    }
}
