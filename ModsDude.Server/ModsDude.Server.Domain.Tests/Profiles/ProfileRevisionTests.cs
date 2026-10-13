using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Profiles;

public class ProfileRevisionTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly ModId _otherModId = new("FS25_OtherMod");
    private static readonly UserId _author = new("author");
    private static readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime _earlier = _now.AddDays(-30);


    [Fact]
    public void A_new_profile_has_no_revision_until_it_is_given_one()
    {
        var profile = CreateProfile();

        Assert.Equal(RevisionNumber.None, profile.HeadRevision);

        profile.CreateRevision([], [], _author, _now, origin: ProfileRevisionOrigin.Created);

        Assert.Equal(new RevisionNumber(1), profile.HeadRevision);
    }

    [Fact]
    public void Saving_moves_the_head_to_the_revision_it_made()
    {
        var profile = CreateProfile();

        profile.CreateRevision([], [], _author, _now, origin: ProfileRevisionOrigin.Created);
        var second = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _now);

        Assert.Equal(second.Number, profile.HeadRevision);
        Assert.Equal(2, second.Number.Value);
    }

    [Fact]
    public void A_revision_records_what_it_pins()
    {
        var profile = CreateProfile();

        var revision = profile.CreateRevision([Pin(_modId, "1.0.0", locked: true), Pin(_otherModId, "2.0.0")], [], _author, _now);

        Assert.Equal(2, revision.ModCount);
        Assert.Equal(
            [(_otherModId, new ModVersionId("2.0.0"), false), (_modId, new ModVersionId("1.0.0"), true)],
            revision.Pins.OrderBy(x => x.ModId.Value).Select(x => (x.ModId, x.VersionId, x.Locked)));
    }

    /// <summary>
    /// The revision stamps its own copies, so a caller's pins - which the save endpoint goes on using
    /// after the revision is made - are never changed, and no pin is shared between two revisions.
    /// </summary>
    [Fact]
    public void A_revision_copies_the_pins_it_is_given()
    {
        var profile = CreateProfile();
        var pin = Pin(_modId, "1.0.0");

        var first = profile.CreateRevision([pin], [], _author, _earlier);
        var second = profile.CreateRevision(first.Pins, first.Pins, _author, _now);

        Assert.NotSame(pin, Assert.Single(first.Pins));
        Assert.Equal(default, pin.Added);
        Assert.NotSame(Assert.Single(first.Pins), Assert.Single(second.Pins));
    }

    [Fact]
    public void A_revision_cannot_pin_one_mod_at_two_versions()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(
            () => profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_modId, "1.1.0")], [], _author, _now));
    }

    [Fact]
    public void A_revision_cannot_pin_one_mod_twice_at_the_same_version()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(
            () => profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_modId, "1.0.0")], [], _author, _now));
    }

    /// <summary>
    /// A failed save must not move the head, or the next one would be refused as stale for a
    /// revision that was never written.
    /// </summary>
    [Fact]
    public void A_refused_revision_does_not_move_the_head()
    {
        var profile = CreateProfile();

        profile.CreateRevision([], [], _author, _now, origin: ProfileRevisionOrigin.Created);

        Assert.Throws<InvalidOperationException>(
            () => profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_modId, "1.1.0")], [], _author, _now));

        Assert.Equal(new RevisionNumber(1), profile.HeadRevision);
    }

    [Fact]
    public void A_label_longer_than_the_maximum_is_refused()
    {
        var profile = CreateProfile();

        Assert.Throws<DomainValidationException>(
            () => profile.CreateRevision([], [], _author, _now, new string('x', ProfileRevision.MaximumLabelLength + 1)));
    }

    [Fact]
    public void A_blank_label_is_no_label()
    {
        var profile = CreateProfile();

        Assert.Null(profile.CreateRevision([], [], _author, _now, "   ").Label);
    }

    [Fact]
    public void A_restore_records_where_it_came_from()
    {
        var profile = CreateProfile();

        var first = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _now, origin: ProfileRevisionOrigin.Created);
        var second = profile.CreateRevision([Pin(_modId, "2.0.0")], first.Pins, _author, _now);

        var restored = profile.CreateRevision(
            first.Pins,
            second.Pins,
            _author,
            _now,
            origin: ProfileRevisionOrigin.Restored,
            sourceRevision: first.Number);

        Assert.Equal(ProfileRevisionOrigin.Restored, restored.Origin);
        Assert.Equal(first.Number, restored.SourceRevision);
        Assert.Equal(new ProfileRevisionChanges(0, 1, 0), restored.Changes);
    }


    [Fact]
    public void A_mod_new_to_the_list_is_added_when_its_revision_was_made()
    {
        var profile = CreateProfile();

        var revision = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _now);

        Assert.Equal(_now, Assert.Single(revision.Pins).Added);
    }

    [Fact]
    public void A_mod_pinned_at_the_same_version_keeps_the_date_it_arrived()
    {
        var profile = CreateProfile();

        var first = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _earlier);
        var revision = profile.CreateRevision([Pin(_modId, "1.0.0")], first.Pins, _author, _now);

        Assert.Equal(_earlier, Assert.Single(revision.Pins).Added);
    }

    /// <summary>
    /// The lock moves no file, so it is not what the date answers: somebody sorting by when a mod
    /// last changed in game must not have a toggled lock reshuffle the list.
    /// </summary>
    [Fact]
    public void Toggling_the_lock_does_not_move_the_date()
    {
        var profile = CreateProfile();

        var first = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _earlier);
        var revision = profile.CreateRevision([Pin(_modId, "1.0.0", locked: true)], first.Pins, _author, _now);

        Assert.Equal(_earlier, Assert.Single(revision.Pins).Added);
    }

    [Fact]
    public void A_mod_that_moved_to_another_version_is_added_again()
    {
        var profile = CreateProfile();

        var first = profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _earlier);
        var revision = profile.CreateRevision([Pin(_modId, "2.0.0")], first.Pins, _author, _now);

        Assert.Equal(_now, Assert.Single(revision.Pins).Added);
    }

    [Fact]
    public void Only_the_mods_that_changed_get_a_new_date()
    {
        var profile = CreateProfile();

        var first = profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_otherModId, "2.0.0")], [], _author, _earlier);
        var revision = profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_otherModId, "3.0.0")], first.Pins, _author, _now);

        var dates = revision.Pins.ToDictionary(x => x.ModId, x => x.Added);

        Assert.Equal(_earlier, dates[_modId]);
        Assert.Equal(_now, dates[_otherModId]);
    }


    [Fact]
    public void A_mod_the_previous_revision_did_not_pin_is_an_addition()
    {
        var changes = ProfileRevisionChanges.Between(
            [],
            [Pin(_modId, "1.0.0")]);

        Assert.Equal(new ProfileRevisionChanges(1, 0, 0), changes);
    }

    [Fact]
    public void A_mod_that_moved_version_is_a_change_rather_than_an_addition_and_a_removal()
    {
        var changes = ProfileRevisionChanges.Between(
            [Pin(_modId, "1.0.0")],
            [Pin(_modId, "2.0.0")]);

        Assert.Equal(new ProfileRevisionChanges(0, 1, 0), changes);
    }

    /// <summary>
    /// Locking a mod is the whole point of some saves, so a save that only toggles one has to count
    /// as a change - otherwise it mints nothing and the decision is silently dropped.
    /// </summary>
    [Fact]
    public void A_mod_whose_lock_was_toggled_is_a_change()
    {
        var changes = ProfileRevisionChanges.Between(
            [Pin(_modId, "1.0.0")],
            [Pin(_modId, "1.0.0", locked: true)]);

        Assert.Equal(new ProfileRevisionChanges(0, 1, 0), changes);
    }

    [Fact]
    public void A_mod_the_new_revision_does_not_pin_is_a_removal()
    {
        var changes = ProfileRevisionChanges.Between(
            [Pin(_modId, "1.0.0")],
            []);

        Assert.Equal(new ProfileRevisionChanges(0, 0, 1), changes);
    }

    [Fact]
    public void An_identical_mod_list_is_no_change_at_all()
    {
        var pins = new[]
        {
            Pin(_modId, "1.0.0", locked: true),
            Pin(_otherModId, "3.0.0")
        };

        Assert.True(ProfileRevisionChanges.Between(pins, pins.Reverse()).IsEmpty);
    }


    private static Profile CreateProfile()
        => new(_repoId, new ProfileName("Test profile"), _now, new ProfileCreateRequestId(Guid.NewGuid()));

    private static ProfileModPin Pin(ModId modId, string versionId, bool locked = false) => new()
    {
        ModId = modId,
        VersionId = new ModVersionId(versionId),
        Locked = locked
    };
}
