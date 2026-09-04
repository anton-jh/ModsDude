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
    private static readonly ProfileId _otherProfileId = new(Guid.NewGuid());
    private static readonly UserId _author = new("author");
    private static readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private const string _hash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string _otherHash = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";


    /// <summary>
    /// The head is <see cref="SavegameVersionNumber.None"/> only between construction and the first
    /// version, which happens in the same transaction - so a savegame that reads as 0 is one nothing
    /// has published yet, never a row somebody could find.
    /// </summary>
    [Fact]
    public void A_new_savegame_has_no_version_until_it_is_given_one()
    {
        var savegame = CreateSavegame();

        Assert.Equal(SavegameVersionNumber.None, savegame.HeadVersion);
        Assert.Equal(0, savegame.HeadVersion.Value);

        savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);

        Assert.Equal(new SavegameVersionNumber(1), savegame.HeadVersion);
    }

    [Fact]
    public void Checking_in_moves_the_head_to_the_version_it_made()
    {
        var savegame = CreateSavegame();

        savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);
        var second = savegame.CreateVersion(_profileId, new RevisionNumber(1), _otherHash, 2048, _author, _now);

        Assert.Equal(second.Number, savegame.HeadVersion);
        Assert.Equal(2, second.Number.Value);
    }

    /// <summary>
    /// A version's revision is what makes a save reproducible, and what lets the client say that a
    /// folder is on a mod list this save was never played against. It is recorded per version rather
    /// than on the savegame, so a save can move from one revision to the next without lying about
    /// what the earlier play actually ran on.
    /// </summary>
    [Fact]
    public void Every_version_records_the_one_revision_it_was_played_on()
    {
        var savegame = CreateSavegame();

        var first = savegame.CreateVersion(_profileId, new RevisionNumber(6), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);
        var second = savegame.CreateVersion(_profileId, new RevisionNumber(7), _otherHash, 1024, _author, _now);

        Assert.Equal(new RevisionNumber(6), first.ProfileRevision);
        Assert.Equal(new RevisionNumber(7), second.ProfileRevision);
        Assert.Equal(_repoId, first.RepoId);
        Assert.Equal(savegame.Id, first.SavegameId);
    }

    /// <summary>
    /// The version names the profile it was played on, and the savegame names the one it follows.
    /// The two are allowed to disagree - branch a profile, move the save onto the branch, and the
    /// older versions still honestly name the old profile's revisions.
    /// </summary>
    [Fact]
    public void A_version_can_name_a_different_profile_from_the_one_the_savegame_follows()
    {
        var savegame = CreateSavegame();

        var version = savegame.CreateVersion(_otherProfileId, new RevisionNumber(1), _hash, 1024, _author, _now);

        Assert.Equal(_otherProfileId, version.ProfileId);
        Assert.Equal(_profileId, savegame.ProfileId);
    }

    /// <summary>
    /// A forced check-in records what was actually played rather than what the head had become, which
    /// is what leaves the fork in the record without anybody needing a tree to read it.
    /// </summary>
    [Fact]
    public void A_forced_check_in_records_the_version_it_was_actually_built_on()
    {
        var savegame = CreateSavegame();

        savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);
        savegame.CreateVersion(_profileId, new RevisionNumber(1), _otherHash, 1024, _author, _now);

        var forced = savegame.CreateVersion(
            _profileId,
            new RevisionNumber(1),
            _hash,
            1024,
            _author,
            _now,
            origin: SavegameVersionOrigin.Forced,
            baseVersion: new SavegameVersionNumber(1));

        Assert.Equal(SavegameVersionOrigin.Forced, forced.Origin);
        Assert.Equal(new SavegameVersionNumber(1), forced.BaseVersion);
        Assert.Equal(new SavegameVersionNumber(3), savegame.HeadVersion);
    }

    /// <summary>
    /// A restore is a version like any other - the same call, differing only in where the bytes came
    /// from. Copying forward rather than reopening is what keeps the history a record of what
    /// happened rather than a mutable pointer.
    /// </summary>
    [Fact]
    public void A_restore_copies_an_old_version_forward_rather_than_reopening_it()
    {
        var savegame = CreateSavegame();

        var original = savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);
        savegame.CreateVersion(_profileId, new RevisionNumber(2), _otherHash, 1024, _author, _now);

        var restored = savegame.CreateVersion(
            _profileId,
            new RevisionNumber(2),
            original.ContentHash,
            original.SizeBytes,
            _author,
            _now,
            origin: SavegameVersionOrigin.Restored,
            baseVersion: original.Number);

        Assert.Equal(SavegameVersionOrigin.Restored, restored.Origin);
        Assert.Equal(new SavegameVersionNumber(3), restored.Number);
        Assert.Equal(original.ContentHash, restored.ContentHash);
        Assert.Equal(new SavegameVersionNumber(1), restored.BaseVersion);
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

        var published = savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);
        var checkedIn = savegame.CreateVersion(_profileId, new RevisionNumber(1), _otherHash, 1024, _author, _now, checkoutId: checkoutId);

        Assert.Null(published.CheckoutId);
        Assert.Equal(checkoutId, checkedIn.CheckoutId);
    }

    [Fact]
    public void A_label_longer_than_the_maximum_is_refused()
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateVersion(
                _profileId, new RevisionNumber(1), _hash, 1024, _author, _now,
                new string('x', SavegameVersion.MaximumLabelLength + 1)));
    }

    [Fact]
    public void A_blank_label_is_no_label()
    {
        var savegame = CreateSavegame();

        Assert.Null(savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, "   ").Label);
    }

    /// <summary>
    /// A label is the gesture by which somebody keeps a version from being pruned, so it has to mean
    /// the same thing whether or not the person typed a stray space around it.
    /// </summary>
    [Fact]
    public void A_label_is_trimmed()
    {
        var savegame = CreateSavegame();

        Assert.Equal(
            "Before the flood",
            savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, "  Before the flood  ").Label);
    }

    /// <summary>
    /// A size is stated so a history can be read without a storage round trip; a save that weighs
    /// nothing is a failed pack, and recording it would put a version in the history whose blob can
    /// never be restored.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_version_that_weighs_nothing_is_refused(long sizeBytes)
    {
        var savegame = CreateSavegame();

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, sizeBytes, _author, _now));
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
            () => savegame.CreateVersion(_profileId, new RevisionNumber(1), contentHash, 1024, _author, _now));
    }

    /// <summary>
    /// A refused check-in must not move the head, or the next one would be refused as stale for a
    /// version that was never written - and the number it burned would leave a hole nothing explains.
    /// </summary>
    [Fact]
    public void A_refused_version_does_not_move_the_head()
    {
        var savegame = CreateSavegame();

        savegame.CreateVersion(_profileId, new RevisionNumber(1), _hash, 1024, _author, _now, origin: SavegameVersionOrigin.Created);

        Assert.Throws<DomainValidationException>(
            () => savegame.CreateVersion(_profileId, new RevisionNumber(1), _otherHash, 0, _author, _now));

        Assert.Equal(new SavegameVersionNumber(1), savegame.HeadVersion);
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
    public void The_first_version_number_is_one()
    {
        Assert.Equal(0, SavegameVersionNumber.None.Value);
        Assert.Equal(new SavegameVersionNumber(1), SavegameVersionNumber.None.Next());
        Assert.Equal(new SavegameVersionNumber(3), new SavegameVersionNumber(2).Next());
    }

    /// <summary>
    /// Numbers exist to be said out loud - "restore version 4" - so they order and print as the
    /// integer somebody read off the history, and pruning leaves the gap rather than renumbering.
    /// </summary>
    [Fact]
    public void Version_numbers_order_and_print_as_the_number_somebody_would_say()
    {
        Assert.True(new SavegameVersionNumber(2).CompareTo(new SavegameVersionNumber(1)) > 0);
        Assert.True(new SavegameVersionNumber(1).CompareTo(new SavegameVersionNumber(2)) < 0);
        Assert.Equal(0, new SavegameVersionNumber(2).CompareTo(new SavegameVersionNumber(2)));

        Assert.Equal("4", new SavegameVersionNumber(4).ToString());
        Assert.Equal("0", SavegameVersionNumber.None.ToString());
    }


    private static Savegame CreateSavegame()
        => new(_repoId, new SavegameName("Big Valley"), _profileId, _now);
}
