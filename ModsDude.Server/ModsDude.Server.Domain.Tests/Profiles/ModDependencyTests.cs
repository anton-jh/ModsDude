using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Profiles;

public class ModDependencyTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly RepoId _otherRepoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");
    private static readonly ModId _otherModId = new("FS25_OtherMod");


    [Fact]
    public void A_profile_depends_on_a_mod_at_the_version_it_was_added_at()
    {
        var profile = CreateProfile();
        var version = CreateVersion(_repoId, _modId, "1.0.0", 0);

        var dependency = profile.AddDependency(version, locked: false);

        Assert.True(profile.HasDependencyOn(_modId));
        Assert.Same(version, dependency.ModVersion);
    }

    [Fact]
    public void A_profile_cannot_depend_on_the_same_mod_at_two_versions()
    {
        var profile = CreateProfile();

        profile.AddDependency(CreateVersion(_repoId, _modId, "1.0.0", 0), locked: false);

        Assert.Throws<InvalidOperationException>(
            () => profile.AddDependency(CreateVersion(_repoId, _modId, "1.1.0", 1), locked: false));
        Assert.Single(profile.ModDependencies);
    }

    [Fact]
    public void A_profile_cannot_depend_on_a_version_from_another_repo()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(
            () => profile.AddDependency(CreateVersion(_otherRepoId, _modId, "1.0.0", 0), locked: false));
        Assert.Empty(profile.ModDependencies);
    }

    [Fact]
    public void Deleting_a_dependency_the_profile_does_not_have_throws()
    {
        var profile = CreateProfile();

        Assert.Throws<InvalidOperationException>(() => profile.DeleteDependency(_modId));
    }


    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void A_dependency_is_effectively_locked_when_either_the_profile_or_the_mod_locks_it(
        bool lockedInProfile, bool lockedOnVersion, bool expected)
    {
        var profile = CreateProfile();
        var version = CreateVersion(_repoId, _modId, "1.0.0", 0, locked: lockedOnVersion);

        var dependency = profile.AddDependency(version, locked: lockedInProfile);

        Assert.Equal(expected, dependency.IsEffectivelyLocked);
    }


    [Fact]
    public void A_dependency_can_be_upgraded_when_a_later_sibling_exists()
    {
        var dependency = CreateDependency("1.0.0", 0);

        var siblings = new[]
        {
            dependency.ModVersion,
            CreateVersion(_repoId, _modId, "1.1.0", 1)
        };

        Assert.True(dependency.CanBeUpgraded(siblings));
    }

    [Fact]
    public void A_dependency_on_the_last_sibling_cannot_be_upgraded()
    {
        var dependency = CreateDependency("1.1.0", 1);

        var siblings = new[]
        {
            CreateVersion(_repoId, _modId, "1.0.0", 0),
            dependency.ModVersion
        };

        Assert.False(dependency.CanBeUpgraded(siblings));
    }

    [Fact]
    public void Upgrading_moves_the_dependency_to_the_last_sibling()
    {
        var dependency = CreateDependency("1.0.0", 0);

        var latest = CreateVersion(_repoId, _modId, "1.2.0", 2);
        var siblings = new[]
        {
            dependency.ModVersion,
            CreateVersion(_repoId, _modId, "1.1.0", 1),
            latest
        };

        dependency.Upgrade(siblings);

        Assert.Same(latest, dependency.ModVersion);
    }

    [Fact]
    public void Upgrading_reads_the_latest_from_the_siblings_it_is_given_rather_than_the_sequence_numbers_alone()
    {
        // The sibling set is the caller's query result, so an upgrade can only ever reach a version
        // that was passed in — even when a higher-numbered one exists elsewhere.
        var dependency = CreateDependency("1.0.0", 0);

        var withheld = CreateVersion(_repoId, _modId, "1.2.0", 2);
        var offered = CreateVersion(_repoId, _modId, "1.1.0", 1);

        dependency.Upgrade([dependency.ModVersion, offered]);

        Assert.Same(offered, dependency.ModVersion);
        Assert.NotSame(withheld, dependency.ModVersion);
    }

    [Fact]
    public void Upgrading_against_an_empty_sibling_set_throws()
    {
        var dependency = CreateDependency("1.0.0", 0);

        Assert.Throws<InvalidOperationException>(() => dependency.Upgrade([]));
    }

    [Fact]
    public void Changing_to_another_version_of_the_same_mod_moves_the_pin()
    {
        var dependency = CreateDependency("1.0.0", 0);
        var newVersion = CreateVersion(_repoId, _modId, "1.1.0", 1);

        dependency.ChangeVersion(newVersion);

        Assert.Same(newVersion, dependency.ModVersion);
    }

    [Fact]
    public void Changing_to_a_version_of_a_different_mod_throws()
    {
        var dependency = CreateDependency("1.0.0", 0);
        var original = dependency.ModVersion;

        Assert.Throws<InvalidOperationException>(
            () => dependency.ChangeVersion(CreateVersion(_repoId, _otherModId, "1.0.0", 0)));
        Assert.Same(original, dependency.ModVersion);
    }

    [Fact]
    public void Changing_to_a_version_from_another_repo_throws()
    {
        var dependency = CreateDependency("1.0.0", 0);

        Assert.Throws<InvalidOperationException>(
            () => dependency.ChangeVersion(CreateVersion(_otherRepoId, _modId, "1.1.0", 1)));
    }

    [Fact]
    public void Changing_the_version_leaves_the_profile_s_lock_in_place()
    {
        var dependency = CreateDependency("1.0.0", 0, locked: true);

        dependency.ChangeVersion(CreateVersion(_repoId, _modId, "1.1.0", 1));

        Assert.True(dependency.Locked);
    }


    private static Profile CreateProfile() => new(_repoId, new ProfileName("profile"), new DateTime(2026, 1, 1));

    private static ModDependency CreateDependency(string versionId, int sequenceNumber, bool locked = false)
        => CreateProfile().AddDependency(CreateVersion(_repoId, _modId, versionId, sequenceNumber), locked);

    private static ModVersion CreateVersion(RepoId repoId, ModId modId, string versionId, int sequenceNumber, bool locked = false) => new()
    {
        RepoId = repoId,
        ModId = modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = sequenceNumber,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = locked,
        Attributes = [],
        Created = default,
        Updated = default
    };
}
