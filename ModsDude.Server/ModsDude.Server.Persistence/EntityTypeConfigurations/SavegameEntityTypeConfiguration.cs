using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Persistence.EntityTypeConfigurations;

/// <summary>
/// The index names the API has to be able to say out loud. A unique violation names the index it
/// broke and nothing else, so an endpoint that has to tell two of them apart needs the name - and a
/// literal in the endpoint could drift away from the one the model actually creates.
/// </summary>
public static class SavegameIndexNames
{
    public const string OneCurrentSavegamePerProfile = "IX_Savegames_OneCurrentPerProfile";
}

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

        // Restrict, not the cascade EF would infer: ProfileId is the standing statement that this
        // save follows that profile, so deleting the profile would leave the save pointing at
        // nothing. The profile a save has actually been played on is held per version and is
        // Restrict too - see SavegameVersionEntityTypeConfiguration - so in practice a profile that
        // has been played is already undeletable. This keeps a profile that was only ever pointed at
        // from slipping through that rule.
        // Optional, because ProfileId is: a savegame published without a mod list points at no
        // profile and constrains nothing.
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
        // Filtered on ArchivedAt, like a profile's: an archived savegame gives up its name, and a
        // clash is resolved when it is restored rather than being prevented forever.
        builder.HasIndex(x => new { x.RepoId, x.Name })
            .IsUnique()
            .HasFilter("\"ArchivedAt\" IS NULL");

        // At most one current savegame per profile, in the database rather than in the endpoints
        // that swap them - the same shape as the one-open-claim index in
        // SavegameCheckoutEntityTypeConfiguration, and there for the same reason: two people
        // publishing to one profile in the same instant produce one current farm and one refusal.
        // It is also what makes a swap have to be ordered, since the instant where both rows are
        // current is exactly what this refuses.
        //
        // Deliberately NOT also filtered on ArchivedAt, unlike the name index directly above.
        // Archiving does not change current or past - it is the repo-wide visibility state, a
        // different fact - so an archived savegame still holds its profile's slot. Copying the
        // filter from the index above would let a second savegame quietly become current behind an
        // archived one, and the profile would then have two.
        //
        // Savegames with no profile fall out of it for free: PostgreSQL treats nulls in a unique
        // index as distinct, so any number of them coexist, which is the intent.
        //
        // It also replaces the plain (RepoId, ProfileId) index EF had made for the foreign key, and
        // being filtered it does not cover a lookup that includes past savegames - "does anything in
        // this repo follow this profile?", asked once by DeleteProfileV1Endpoint. A repo holds a
        // handful of savegames, so that is a scan of nothing; an unfiltered second index over the
        // same two columns would be carried by every write to buy it.
        builder.HasIndex(x => new { x.RepoId, x.ProfileId })
            .IsUnique()
            .HasFilter("\"SupersededAt\" IS NULL")
            // Named, unlike every other index here, because the API reads this name back off a
            // unique violation to say which rule was broken - a publish can lose to a name clash or
            // to somebody else's publish in the same instant, and the two need different sentences.
            .HasDatabaseName(SavegameIndexNames.OneCurrentSavegamePerProfile);

        // Superseded means "this profile is following some other farm now", which is a sentence
        // about a profile. A savegame that follows none is neither current nor past, so a stamp on
        // one would say nothing - and would then be read as a state by everything that asks.
        builder.ToTable(x => x.HasCheckConstraint(
            "CK_Savegames_SupersededOnlyWithAProfile",
            "\"SupersededAt\" IS NULL OR \"ProfileId\" IS NOT NULL"));
    }
}
