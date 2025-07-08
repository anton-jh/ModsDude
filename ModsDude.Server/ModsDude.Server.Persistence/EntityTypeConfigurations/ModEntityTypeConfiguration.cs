using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModEntityTypeConfiguration : IEntityTypeConfiguration<Mod>
{
    public void Configure(EntityTypeBuilder<Mod> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.Id });

        builder.HasOne<Repo>().WithMany().HasForeignKey(x => x.RepoId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(x => x.Versions).WithOne(x => x.Mod).HasForeignKey(
            ModVersionShadowProperties.RepoId,
            ModVersionShadowProperties.ModId)
            .IsRequired();

        builder.Navigation(x => x.Versions).AutoInclude();
    }
}
