using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class ProfileIgnoredModEntityTypeConfiguration : IEntityTypeConfiguration<ProfileIgnoredMod>
{
    public void Configure(EntityTypeBuilder<ProfileIgnoredMod> builder)
    {
        builder.HasKey(x => new { x.RepoId, x.ProfileId, x.ModId });

        // Cascade: a deleted profile takes its ignored mods with it, and so does a deleted repo by
        // way of its profiles. There is no foreign key onto the mod - see ProfileIgnoredMod.
        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
