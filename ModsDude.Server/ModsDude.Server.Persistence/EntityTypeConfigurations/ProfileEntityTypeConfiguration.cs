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
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId);

            // Backs the one-version-per-mod rule that Profile.AddDependency enforces in the domain.
            // Without it a concurrent double-add pins one mod at two versions, which the sync engine
            // has no way to resolve.
            modDependency.HasIndex(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId,
                ModDependencyShadowProperties.ModId)
                .IsUnique();
        });

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
