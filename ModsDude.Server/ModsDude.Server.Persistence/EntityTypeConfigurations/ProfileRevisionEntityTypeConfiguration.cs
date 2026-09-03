using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ProfileRevisionEntityTypeConfiguration : IEntityTypeConfiguration<ProfileRevision>
{
    public void Configure(EntityTypeBuilder<ProfileRevision> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.ProfileId, x.Number });

        // Cascade: a deleted profile takes its history with it. Nothing else points at a revision,
        // and a history whose profile is gone is not a record of anything.
        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(x => x.Origin).HasConversion<string>();
        builder.Property(x => x.Label).HasMaxLength(ProfileRevision.MaximumLabelLength);

        builder.ComplexProperty(x => x.Changes);

        builder.OwnsMany(x => x.ModDependencies, modDependency =>
        {
            modDependency.WithOwner().HasForeignKey(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId,
                ModDependencyShadowProperties.RevisionNumber);

            // Restrict, not the cascade EF would infer: deleting a version a revision pins would
            // otherwise rewrite history behind everyone's back, and the delete endpoints refuse
            // exactly that case. Restrict makes the database enforce the same rule, so a dependency
            // added between the endpoint's check and its commit fails loudly instead of being swept
            // away. With history, this holds every version a profile has ever pinned - see
            // docs/02-domain-model.md#a-pinned-version-cannot-be-deleted-any-more.
            modDependency.HasOne(x => x.ModVersion).WithMany().HasForeignKey(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId)
                .OnDelete(DeleteBehavior.Restrict);

            modDependency.HasKey(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId,
                ModDependencyShadowProperties.RevisionNumber,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId);

            // Backs the one-version-per-mod rule that ProfileRevision's constructor enforces.
            // Without it a concurrent double-write pins one mod at two versions, which the sync
            // engine has no way to resolve.
            modDependency.HasIndex(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ProfileId,
                ModDependencyShadowProperties.RevisionNumber,
                ModDependencyShadowProperties.ModId)
                .IsUnique();

            // Answers "does any revision anywhere in this repo still pin this version?", which is
            // what stands between a mod version and deletion. Without it that question scans every
            // dependency row in the repo - profiles times revisions times thousands of mods.
            modDependency.HasIndex(
                ModDependencyShadowProperties.RepoId,
                ModDependencyShadowProperties.ModId,
                ModDependencyShadowProperties.ModVersionId);
        });
    }
}

internal static class ModDependencyShadowProperties
{
    public const string ProfileId = "ProfileId";
    public const string RepoId = "RepoId";
    public const string RevisionNumber = "RevisionNumber";
    public const string ModId = "ModId";
    public const string ModVersionId = "ModVersionId";
}
