using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Savegames;

public class SavegameTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ProfileId _profileId = new(Guid.NewGuid());
    private static readonly UserId _author = new("author");
    private static readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";


    /// <summary>
    /// The head is <see cref="SavegameSnapshotNumber.None"/> only between construction and the first
    /// snapshot, which happens in the same transaction - so a savegame that reads as 0 is one nothing
    /// has published yet, never a row somebody could find.
    /// </summary>
    [Fact]
    public void A_new_savegame_has_no_snapshot_until_it_is_given_one()
    {
        var savegame = CreateSavegame();

        Assert.Equal(SavegameSnapshotNumber.None, savegame.HeadSnapshot);
        Assert.Equal(0, savegame.HeadSnapshot.Value);

        savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);

        Assert.Equal(new SavegameSnapshotNumber(1), savegame.HeadSnapshot);
    }

    [Fact]
    public void Checking_in_moves_the_head_to_the_snapshot_it_made()
    {
        var savegame = CreateSavegame();

        savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);
        var second = savegame.CreateSnapshot(new RevisionNumber(1), _otherHash, 2048, _author, _now);

        Assert.Equal(second.Number, savegame.HeadSnapshot);
        Assert.Equal(2, second.Number.Value);
    }

    /// <summary>
    /// A snapshot's revision is what makes a save reproducible, and what lets the client say that a
    /// folder is on a mod list this save was never played against. It is recorded per snapshot rather
    /// than on the savegame, so a save can move from one revision to the next without lying about
    /// what the earlier play actually ran on.
    /// </summary>
    [Fact]
    public void Every_snapshot_records_the_one_revision_it_was_played_on()
    {
        var savegame = CreateSavegame();

        var first = savegame.CreateSnapshot(new RevisionNumber(6), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);
        var second = savegame.CreateSnapshot(new RevisionNumber(7), _otherHash, 1024, _author, _now);

        Assert.Equal(new RevisionNumber(6), first.ProfileRevision);
        Assert.Equal(new RevisionNumber(7), second.ProfileRevision);
        Assert.Equal(_repoId, first.RepoId);
        Assert.Equal(savegame.Id, first.SavegameId);
    }

    /// <summary>
    /// The snapshot's profile is the savegame's, taken rather than passed. Nothing moves a save
    /// between profiles, so a caller able to name one could only ever disagree with the row - and
    /// two profiles' revision numbers mean nothing to each other, so the disagreement would be
    /// unreadable rather than merely wrong.
    /// </summary>
    [Fact]
    public void A_snapshot_names_the_profile_its_savegame_follows()
    {
        var savegame = CreateSavegame();

        var snapshot = savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now);

        Assert.Equal(_profileId, snapshot.ProfileId);
        Assert.Equal(_profileId, savegame.ProfileId);
    }

    /// <summary>
    /// A savegame published without a mod list records no revision on any of its snapshots. It is
    /// unmanaged by the publisher's choice, and a null revision is what says so.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_profile_makes_snapshots_that_name_no_revision()
    {
        var savegame = CreateSavegameWithNoProfile();

        var snapshot = savegame.CreateSnapshot(null, _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);

        Assert.Null(snapshot.ProfileId);
        Assert.Null(snapshot.ProfileRevision);
        Assert.Equal(new SavegameSnapshotNumber(1), savegame.HeadSnapshot);
    }

    /// <summary>
    /// The pairing, from both sides. A revision without the profile that numbered it is unreadable,
    /// and a profile without a revision leaves the one warning that matters - your folder is on a
    /// list this save has never seen - unanswerable. Both halves are refused before the check
    /// constraint behind them ever sees the row.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_profile_refuses_a_snapshot_that_names_a_revision()
    {
        var savegame = CreateSavegameWithNoProfile();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now));

        Assert.Equal(SavegameSnapshotNumber.None, savegame.HeadSnapshot);
    }

    /// <inheritdoc cref="A_savegame_with_no_profile_refuses_a_snapshot_that_names_a_revision"/>
    [Fact]
    public void A_savegame_with_a_profile_refuses_a_snapshot_that_names_no_revision()
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(null, _hash, 1024, _author, _now));

        Assert.Equal(SavegameSnapshotNumber.None, savegame.HeadSnapshot);
    }


    /// <summary>
    /// A savegame is its profile's current one from the moment it is published, without anybody
    /// saying so. Current is the unmarked default; past is the state something has to do.
    /// </summary>
    [Fact]
    public void A_new_savegame_is_its_profiles_current_one()
    {
        var savegame = CreateSavegame();

        Assert.True(savegame.IsCurrent);
        Assert.False(savegame.IsPast);
        Assert.Null(savegame.SupersededAt);
    }

    /// <summary>
    /// Neither word applies to a savegame that follows no mod list. It is in no succession, so
    /// reading it as current would put it in one - and would make it the answer to "which savegame is
    /// this profile following?" for a profile it has nothing to do with.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_profile_is_neither_current_nor_past()
    {
        var savegame = CreateSavegameWithNoProfile();

        Assert.False(savegame.IsCurrent);
        Assert.False(savegame.IsPast);
        Assert.Null(savegame.SupersededAt);
    }

    [Fact]
    public void Superseding_makes_a_savegame_past()
    {
        var savegame = CreateSavegame();

        savegame.Supersede(_now);

        Assert.True(savegame.IsPast);
        Assert.False(savegame.IsCurrent);
        Assert.Equal(_now, savegame.SupersededAt);
    }

    /// <summary>
    /// The stamp says when the profile moved on, and a second caller saying so did not move it -
    /// the same rule <see cref="Savegame.Archive"/> follows, and for the same reason.
    /// </summary>
    [Fact]
    public void Superseding_twice_does_not_restamp()
    {
        var savegame = CreateSavegame();

        savegame.Supersede(_now);
        savegame.Supersede(_now.AddDays(30));

        Assert.Equal(_now, savegame.SupersededAt);
    }

    /// <summary>
    /// The other half of the swap. A past savegame is not read-only and never was; making it current
    /// again changes only which revision it runs on.
    /// </summary>
    [Fact]
    public void A_past_savegame_can_be_made_current_again()
    {
        var savegame = CreateSavegame();

        savegame.Supersede(_now);
        savegame.MakeCurrent();

        Assert.True(savegame.IsCurrent);
        Assert.Null(savegame.SupersededAt);
    }

    /// <summary>
    /// Neither half of the swap means anything for a savegame in no succession, and both refuse
    /// rather than writing a stamp nothing could read. The database says the same thing underneath,
    /// with a check constraint.
    /// </summary>
    [Fact]
    public void A_savegame_with_no_profile_can_be_neither_superseded_nor_made_current()
    {
        var savegame = CreateSavegameWithNoProfile();

        Assert.Throws<InvalidOperationException>(() => savegame.Supersede(_now));
        Assert.Throws<InvalidOperationException>(savegame.MakeCurrent);

        Assert.Null(savegame.SupersededAt);
    }

    /// <summary>
    /// Two unrelated facts. Archiving is the repo-wide visibility state; past is which savegame a profile
    /// follows. A profile whose current savegame is archived still has a current savegame, which is
    /// the case that has to be said out loud rather than quietly resolved.
    /// </summary>
    [Fact]
    public void Archiving_does_not_change_current_or_past()
    {
        var savegame = CreateSavegame();

        savegame.Archive(_now);

        Assert.True(savegame.IsArchived);
        Assert.True(savegame.IsCurrent);

        savegame.Restore();

        Assert.True(savegame.IsCurrent);
    }

    /// <summary>
    /// A forced check-in records what was actually played rather than what the head had become, which
    /// is what leaves the fork in the record without anybody needing a tree to read it.
    /// </summary>
    [Fact]
    public void A_forced_check_in_records_the_snapshot_it_was_actually_built_on()
    {
        var savegame = CreateSavegame();

        savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);
        savegame.CreateSnapshot(new RevisionNumber(1), _otherHash, 1024, _author, _now);

        var forced = savegame.CreateSnapshot(
            new RevisionNumber(1),
            _hash,
            1024,
            _author,
            _now,
            origin: SavegameSnapshotOrigin.Forced,
            baseSnapshot: new SavegameSnapshotNumber(1));

        Assert.Equal(SavegameSnapshotOrigin.Forced, forced.Origin);
        Assert.Equal(new SavegameSnapshotNumber(1), forced.BaseSnapshot);
        Assert.Equal(new SavegameSnapshotNumber(3), savegame.HeadSnapshot);
    }

    /// <summary>
    /// A restore is a snapshot like any other - the same call, differing only in where the bytes came
    /// from. Copying forward rather than reopening is what keeps the history a record of what
    /// happened rather than a mutable pointer.
    /// </summary>
    [Fact]
    public void A_restore_copies_an_old_snapshot_forward_rather_than_reopening_it()
    {
        var savegame = CreateSavegame();

        var original = savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);
        savegame.CreateSnapshot(new RevisionNumber(2), _otherHash, 1024, _author, _now);

        var restored = savegame.CreateSnapshot(
            new RevisionNumber(2),
            original.ContentHash,
            original.SizeBytes,
            _author,
            _now,
            origin: SavegameSnapshotOrigin.Restored,
            baseSnapshot: original.Number);

        Assert.Equal(SavegameSnapshotOrigin.Restored, restored.Origin);
        Assert.Equal(new SavegameSnapshotNumber(3), restored.Number);
        Assert.Equal(original.ContentHash, restored.ContentHash);
        Assert.Equal(new SavegameSnapshotNumber(1), restored.BaseSnapshot);
    }

    /// <summary>
    /// The checkout is what joins the two halves of a savegame's history into one timeline; a publish
    /// and a forced check-in made without holding the save have nothing to join to.
    /// </summary>
    [Fact]
    public void A_check_in_records_the_checkout_it_was_made_against()
    {
        var savegame = CreateSavegame();
        var checkoutId = new SavegameCheckoutId(Guid.NewGuid());

        var published = savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);
        var checkedIn = savegame.CreateSnapshot(new RevisionNumber(1), _otherHash, 1024, _author, _now, checkoutId: checkoutId);

        Assert.Null(published.CheckoutId);
        Assert.Equal(checkoutId, checkedIn.CheckoutId);
    }

    [Fact]
    public void A_label_longer_than_the_maximum_is_refused()
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(
                new RevisionNumber(1), _hash, 1024, _author, _now,
                new string('x', SavegameSnapshot.MaximumLabelLength + 1)));
    }

    [Fact]
    public void A_blank_label_is_no_label()
    {
        var savegame = CreateSavegame();

        Assert.Null(savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, "   ").Label);
    }

    /// <summary>
    /// A label is the gesture by which somebody keeps a snapshot from being pruned, so it has to mean
    /// the same thing whether or not the person typed a stray space around it.
    /// </summary>
    [Fact]
    public void A_label_is_trimmed()
    {
        var savegame = CreateSavegame();

        Assert.Equal(
            "Before the flood",
            savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, "  Before the flood  ").Label);
    }

    /// <summary>
    /// A size is stated so a history can be read without a storage round trip; a save that weighs
    /// nothing is a failed pack, and recording it would put a snapshot in the history whose blob can
    /// never be restored.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_snapshot_that_weighs_nothing_is_refused(long sizeBytes)
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(new RevisionNumber(1), _hash, sizeBytes, _author, _now));
    }

    /// <summary>
    /// The hash is the blob path segment the bytes live at, not a checksum carried alongside them, so
    /// anything that is not a lowercase hex SHA-256 addresses something that is not a savegame.
    /// </summary>
    [Theory]
    [InlineData("not-a-hash")]
    [InlineData("")]
    [InlineData("e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b85")]
    [InlineData("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855")]
    public void A_content_hash_that_is_not_an_address_is_refused(string contentHash)
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(new RevisionNumber(1), contentHash, 1024, _author, _now));
    }

    /// <summary>
    /// A refused check-in must not move the head, or the next one would be refused as stale for a
    /// snapshot that was never written - and the number it burned would leave a hole nothing explains.
    /// </summary>
    [Fact]
    public void A_refused_snapshot_does_not_move_the_head()
    {
        var savegame = CreateSavegame();

        savegame.CreateSnapshot(new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameSnapshotOrigin.Created);

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateSnapshot(new RevisionNumber(1), _otherHash, 0, _author, _now));

        Assert.Equal(new SavegameSnapshotNumber(1), savegame.HeadSnapshot);
    }


    [Fact]
    public void A_savegame_name_is_trimmed()
    {
        Assert.Equal("Big Valley", new SavegameName("  Big Valley  ").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_savegame_must_have_a_name(string value)
    {
        Assert.Throws<DomainValidationException>(() => new SavegameName(value));
    }

    /// <summary>
    /// The name is read by people picking a save to play, so the only things worth refusing are an
    /// empty one and an essay.
    /// </summary>
    [Fact]
    public void A_savegame_name_longer_than_the_maximum_is_refused()
    {
        Assert.Throws<DomainValidationException>(() => new SavegameName(new string('x', SavegameName.MaximumLength + 1)));

        Assert.Equal(
            SavegameName.MaximumLength,
            new SavegameName(new string('x', SavegameName.MaximumLength)).Value.Length);
    }


    [Fact]
    public void The_first_snapshot_number_is_one()
    {
        Assert.Equal(0, SavegameSnapshotNumber.None.Value);
        Assert.Equal(new SavegameSnapshotNumber(1), SavegameSnapshotNumber.None.Next());
        Assert.Equal(new SavegameSnapshotNumber(3), new SavegameSnapshotNumber(2).Next());
    }

    /// <summary>
    /// Numbers exist to be said out loud - "restore snapshot 4" - so they order and print as the
    /// integer somebody read off the history, and pruning leaves the gap rather than renumbering.
    /// </summary>
    [Fact]
    public void Snapshot_numbers_order_and_print_as_the_number_somebody_would_say()
    {
        Assert.True(new SavegameSnapshotNumber(2).CompareTo(new SavegameSnapshotNumber(1)) > 0);
        Assert.True(new SavegameSnapshotNumber(1).CompareTo(new SavegameSnapshotNumber(2)) < 0);
        Assert.Equal(0, new SavegameSnapshotNumber(2).CompareTo(new SavegameSnapshotNumber(2)));

        Assert.Equal("4", new SavegameSnapshotNumber(4).ToString());
        Assert.Equal("0", SavegameSnapshotNumber.None.ToString());
    }


    private static Savegame CreateSavegame()
        => new(_repoId, new SavegameName("Big Valley"), _profileId, _now);

    private static Savegame CreateSavegameWithNoProfile()
        => new(_repoId, new SavegameName("Big Valley"), null, _now);
}
