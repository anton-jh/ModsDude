using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Profiles;

public class ProfileRevisionTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly RepoId _otherRepoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly ModId _otherModId = new("FS25_OtherMod");
    private static readonly UserId _author = new("author");
    private static readonly DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);


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

        var revision = profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_otherModId, "2.0.0")], [], _author, _now);

        Assert.Equal(2, revision.ModCount);
        Assert.Equal(
            [(_otherModId, new ModVersionId("2.0.0")), (_modId, new ModVersionId("1.0.0"))],
            revision.ToPins().OrderBy(x => x.ModId.Value).Select(x => (x.ModId, x.VersionId)));
    }

    [Fact]
    public void A_revision_cannot_pin_one_mod_at_two_versions()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(
            () => profile.CreateRevision([Pin(_modId, "1.0.0"), Pin(_modId, "1.1.0")], [], _author, _now));
    }

    [Fact]
    public void A_revision_cannot_pin_a_version_from_another_repo()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(
            () => profile.CreateRevision([Pin(_modId, "1.0.0", _otherRepoId)], [], _author, _now));
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

        profile.CreateRevision([Pin(_modId, "1.0.0")], [], _author, _now, origin: ProfileRevisionOrigin.Created);

        var restored = profile.CreateRevision(
            [Pin(_modId, "1.0.0")],
            [new ProfileModPin(_modId, new ModVersionId("2.0.0"), false)],
            _author,
            _now,
            origin: ProfileRevisionOrigin.Restored,
            sourceRevision: new RevisionNumber(1));

        Assert.Equal(ProfileRevisionOrigin.Restored, restored.Origin);
        Assert.Equal(new RevisionNumber(1), restored.SourceRevision);
    }


    [Fact]
    public void A_mod_the_previous_revision_did_not_pin_is_an_addition()
    {
        var changes = ProfileRevisionChanges.Between(
            [],
            [new ProfileModPin(_modId, new ModVersionId("1.0.0"), false)]);

        Assert.Equal(new ProfileRevisionChanges(1, 0, 0), changes);
    }

    [Fact]
    public void A_mod_that_moved_version_is_a_change_rather_than_an_addition_and_a_removal()
    {
        var changes = ProfileRevisionChanges.Between(
            [new ProfileModPin(_modId, new ModVersionId("1.0.0"), false)],
            [new ProfileModPin(_modId, new ModVersionId("2.0.0"), false)]);

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
            [new ProfileModPin(_modId, new ModVersionId("1.0.0"), false)],
            [new ProfileModPin(_modId, new ModVersionId("1.0.0"), true)]);

        Assert.Equal(new ProfileRevisionChanges(0, 1, 0), changes);
    }

    [Fact]
    public void A_mod_the_new_revision_does_not_pin_is_a_removal()
    {
        var changes = ProfileRevisionChanges.Between(
            [new ProfileModPin(_modId, new ModVersionId("1.0.0"), false)],
            []);

        Assert.Equal(new ProfileRevisionChanges(0, 0, 1), changes);
    }

    [Fact]
    public void An_identical_mod_list_is_no_change_at_all()
    {
        var pins = new[]
        {
            new ProfileModPin(_modId, new ModVersionId("1.0.0"), true),
            new ProfileModPin(_otherModId, new ModVersionId("3.0.0"), false)
        };

        Assert.True(ProfileRevisionChanges.Between(pins, pins.Reverse()).IsEmpty);
    }


    private static Profile CreateProfile()
        => new(_repoId, new ProfileName("Test profile"), _now);

    private static ModDependency Pin(ModId modId, string versionId, RepoId? repoId = null, bool locked = false)
        => new()
        {
            ModVersion = CreateVersion(repoId ?? _repoId, modId, versionId),
            Locked = locked
        };

    private static ModVersion CreateVersion(RepoId repoId, ModId modId, string versionId) => new()
    {
        RepoId = repoId,
        ModId = modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = 0,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = false,
        Attributes = [],
        Created = _now,
        Updated = _now
    };
}
