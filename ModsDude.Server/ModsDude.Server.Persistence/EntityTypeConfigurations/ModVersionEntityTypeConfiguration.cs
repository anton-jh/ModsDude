using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ModVersionEntityTypeConfiguration : IEntityTypeConfiguration<ModVersion>
{
    public void Configure(EntityTypeBuilder<ModVersion> builder)
    {
        builder.Property<RepoId>(ModVersionShadowProperties.RepoId);
        builder.Property<ModId>(ModVersionShadowProperties.ModId);

        builder.HasKey(
            ModVersionShadowProperties.RepoId,
            ModVersionShadowProperties.ModId,
            nameof(ModVersion.Id));

        builder.HasOne(x => x.Mod).WithMany(x => x.Versions).HasForeignKey(
            ModVersionShadowProperties.RepoId,
            ModVersionShadowProperties.ModId);

        builder.OwnsMany(x => x.Attributes);

        builder.HasIndex(
            ModVersionShadowProperties.RepoId,
            ModVersionShadowProperties.ModId,
            nameof(ModVersion.SequenceNumber))
            .IsUnique();
    }
}

internal static class ModVersionShadowProperties
{
    public const string RepoId = "RepoId";
    public const string ModId = "ModId";
}
