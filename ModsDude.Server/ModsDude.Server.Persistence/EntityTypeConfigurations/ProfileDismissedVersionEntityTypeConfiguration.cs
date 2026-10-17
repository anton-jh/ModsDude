using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ProfileDismissedVersionEntityTypeConfiguration : IEntityTypeConfiguration<ProfileDismissedVersion>
{
    public void Configure(EntityTypeBuilder<ProfileDismissedVersion> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.ProfileId, x.ModId, x.VersionId });

        builder.Ignore(x => x.Key);

        // Cascade: a deleted profile takes its dismissals with it, and so does a deleted repo by way of
        // its profiles. There is no foreign key onto the mod or the version - see ProfileDismissedVersion.
        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
