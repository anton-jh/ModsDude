using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Activity;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class GameActivityEntityTypeConfiguration : IEntityTypeConfiguration<GameActivity>
{
    public void Configure(EntityTypeBuilder<GameActivity> builder)
    {
        // One row per person per game: the row is what they are on now, not a history of it.
        builder.HasKey(x => new { x.UserId, x.Game });

        builder.Property(x => x.Game).HasMaxLength(GameKey.MaximumLength);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade: a deleted profile is one nobody can be on any more, and a row naming it would be a
        // friend on a mod list the reader cannot open. A deleted repo takes its profiles, so this too.
        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Cascade);

        // No foreign key onto the savegame: it only names what was checked out, and a savegame being
        // deleted afterwards does not make it untrue that somebody checked it out.
        builder.Property(x => x.Kind).HasConversion<string>();

        // Reads everybody's rows in the repos one person is in, over the last week.
        builder.HasIndex(x => new { x.RepoId, x.TouchedAt });
    }
}
