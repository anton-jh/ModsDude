using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
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

        builder.ConfigureDeletionSchedule(x => x.DeletionScheduledFor, x => x.DeletionReason);

        builder.OwnsMany(x => x.Pins, pin =>
        {
            pin.WithOwner().HasForeignKey(
                ProfileModPinShadowProperties.RepoId,
                ProfileModPinShadowProperties.ProfileId,
                ProfileModPinShadowProperties.RevisionNumber);

            pin.Property(x => x.VersionId).HasColumnName("ModVersionId");

            // Restrict, not the cascade EF would infer: deleting a version a revision pins would
            // otherwise rewrite history behind everyone's back, and the delete endpoints refuse
            // exactly that case. Restrict makes the database enforce the same rule, so a pin added
            // between the endpoint's check and its commit fails loudly instead of being swept away.
            // Sharing the revision's RepoId column is what keeps a pin inside its revision's repo.
            pin.HasOne<ModVersion>().WithMany().HasForeignKey(
                ProfileModPinShadowProperties.RepoId,
                nameof(ProfileModPin.ModId),
                nameof(ProfileModPin.VersionId))
                .OnDelete(DeleteBehavior.Restrict);

            pin.HasKey(
                ProfileModPinShadowProperties.RepoId,
                ProfileModPinShadowProperties.ProfileId,
                ProfileModPinShadowProperties.RevisionNumber,
                nameof(ProfileModPin.ModId),
                nameof(ProfileModPin.VersionId));

            // Backs the one-version-per-mod rule that ProfileRevision's constructor enforces.
            // Without it a concurrent double-write pins one mod at two versions, which the sync
            // engine has no way to resolve.
            pin.HasIndex(
                ProfileModPinShadowProperties.RepoId,
                ProfileModPinShadowProperties.ProfileId,
                ProfileModPinShadowProperties.RevisionNumber,
                nameof(ProfileModPin.ModId))
                .IsUnique();

            // Answers "does any revision anywhere in this repo still pin this version?", which is
            // what stands between a mod version and deletion. Without it that question scans every
            // pin row in the repo - profiles times revisions times thousands of mods.
            pin.HasIndex(
                ProfileModPinShadowProperties.RepoId,
                nameof(ProfileModPin.ModId),
                nameof(ProfileModPin.VersionId));
        });
    }
}

internal static class ProfileModPinShadowProperties
{
    public const string ProfileId = "ProfileId";
    public const string RepoId = "RepoId";
    public const string RevisionNumber = "RevisionNumber";
}
