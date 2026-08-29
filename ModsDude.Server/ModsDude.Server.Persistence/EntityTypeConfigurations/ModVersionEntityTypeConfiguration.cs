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
        });

        builder.HasIndex(x => new { x.RepoId, x.ModId, x.SequenceNumber }).IsUnique();
    }
}
