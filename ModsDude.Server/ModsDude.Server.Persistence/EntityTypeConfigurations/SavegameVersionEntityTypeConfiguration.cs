using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegameVersionEntityTypeConfiguration : IEntityTypeConfiguration<SavegameVersion>
{
    public void Configure(EntityTypeBuilder<SavegameVersion> builder)
    {
        // This key is the concurrency control for check-ins, not merely an identity. Two people
        // holding the same head both compute the same next number, so both insert the same
        // (RepoId, SavegameId, Number) and exactly one commits - the loser sees a unique violation
        // rather than silently overwriting the other's play. Without it the numbers stay unique only
        // as long as no two check-ins overlap, which is precisely the case this whole aggregate
        // exists for.
        builder.HasKey(x => new { x.RepoId, x.SavegameId, x.Number });

        // Cascade: a deleted savegame takes its history with it. Nothing outside the savegame
        // addresses a version, and a history whose savegame is gone is not a record of anything.
        builder.HasOne<Savegame>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.SavegameId })
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not the cascade EF would infer: a version names the one mod list it was played
        // against, and deleting that revision would leave the save claiming to be reproducible
        // against a list nobody can read any more. The consequence to accept knowingly is that a
        // profile that has been played can no longer be deleted - the same bargain as
        // ModDependency -> ModVersion one aggregate down, which
        // ProfileRevisionEntityTypeConfiguration makes for the same reason. The delete endpoints
        // should report it the way CheckIfVersionIsDependedOn reports its own, so this fires only
        // for a version checked in between the check and the commit.
        builder.HasOne<ProfileRevision>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId, x.ProfileRevision })
            .OnDelete(DeleteBehavior.Restrict);

        // Fixed length, because it is a SHA-256 in lowercase hex or it is not a blob address.
        // Bounded but not fixed-length. bpchar compares with trailing spaces stripped, which is a
        // semantic nobody wants standing between two hashes, and ModVersion.ContentHash - the same
        // kind of value - is a plain string. Two hashes should differ for the only reason hashes
        // differ.
        builder.Property(x => x.ContentHash).HasMaxLength(ModImageHash.Length);

        builder.Property(x => x.Origin).HasConversion<string>();
        builder.Property(x => x.Label).HasMaxLength(SavegameVersion.MaximumLabelLength);

        // Answers "which blob addresses are still referred to?" for the reclamation sweep, which
        // reads every version in the system and must not do it by scanning them. Deliberately not
        // unique: a version's bytes are addressed by content, so a restore copies an old version
        // forward under the same hash and several versions legitimately share one blob.
        builder.HasIndex(x => new { x.RepoId, x.SavegameId, x.ContentHash });
    }
}
