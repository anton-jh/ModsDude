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

        builder.OwnsMany(x => x.ModDependencies, modDependency =>
        {
            modDependency.WithOwner().HasForeignKey(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId);

            modDependency.HasOne(x => x.ModVersion).WithMany().HasForeignKey(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId);

            modDependency.HasKey(
                ModDependencyShadowProperties.ProfileId, // todo: repoid first
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId);
        }); // todo: unique index on repoid,profileid,modid

        builder.Property(x => x.Name);
        builder.Property(x => x.Created);

        builder.HasIndex(x => new { x.RepoId, x.Name }).IsUnique();
    }
}

internal static class ModDependencyShadowProperties
{
    public const string ProfileId = "ProfileId";
    public const string RepoId = "RepoId";
    public const string ModId = "ModId";
    public const string ModVersionId = "ModVersionId";
}
