using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;
internal class SavegameEntityTypeConfiguration : IEntityTypeConfiguration<Savegame>
{
    public void Configure(EntityTypeBuilder<Savegame> builder)
    {
        // Keyed by the repo, not by a profile. A savegame sits beside profiles rather than under
        // one, which is the placement Profile itself has and for the same reasons.
        builder.HasKey(x => new { x.RepoId, x.Id });

        // Cascade: a deleted repo takes its savegames with it. A savegame outside a repo is not
        // addressable by anything, and the blobs behind it are reclaimed by the sweep afterwards.
        builder.HasOne<Repo>()
            .WithMany()
            .HasForeignKey(x => x.RepoId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict, not the cascade EF would infer: ProfileId is the standing intent that this save
        // follows that profile, so deleting the profile would leave the save pointing at nothing.
        // The profile a save has actually been played on is held per version and is Restrict too -
        // see SavegameVersionEntityTypeConfiguration - so in practice a profile that has been played
        // is already undeletable. This keeps a profile that was only ever pointed at from slipping
        // through that rule.
        builder.HasOne<Profile>()
            .WithMany()
            .HasForeignKey(x => new { x.RepoId, x.ProfileId })
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.Name).HasMaxLength(SavegameName.MaximumLength);
        builder.Property(x => x.Created);

        // A scalar, and there is deliberately no navigation to the versions themselves - see
        // Savegame for why loading a savegame must not be able to drag its history in with it.
        builder.Property(x => x.HeadVersion);

        // A savegame's name is what people say to each other, so it has to mean one thing inside a
        // repo. Unique rather than checked in the endpoint, so two people publishing "Season 4" at
        // the same moment produce one savegame and one refusal.
        builder.HasIndex(x => new { x.RepoId, x.Name }).IsUnique();
    }
}
