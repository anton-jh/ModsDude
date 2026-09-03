using ModsDude.Server.Domain.Mods;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Profiles;

/// <summary>
/// What is left of a dependency now that it belongs to an immutable revision: the two locks, and
/// the lightweight form a comparison reads it in. Moving a pin is no longer something a dependency
/// does - it is a new revision, covered by <see cref="ProfileRevisionTests"/>.
/// </summary>
public class ModDependencyTests
{
    private static readonly RepoId _repoId = new(Guid.NewGuid());
    private static readonly ModId _modId = new("FS25_TestMod");


    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    public void A_dependency_is_effectively_locked_when_either_the_profile_or_the_mod_locks_it(
        bool lockedInProfile, bool lockedOnVersion, bool expected)
    {
        var dependency = new ModDependency
        {
            ModVersion = CreateVersion("1.0.0", locked: lockedOnVersion),
            Locked = lockedInProfile
        };

        Assert.Equal(expected, dependency.IsEffectivelyLocked);
    }

    /// <summary>
    /// The adapter's lock is deliberately absent from the pin. It belongs to the mod version rather
    /// than to the profile, so a save that carried it would be writing a claim it has no standing to
    /// make - and two revisions would read as different because a mod was re-registered.
    /// </summary>
    [Fact]
    public void A_pin_carries_the_profile_s_lock_and_not_the_mod_s()
    {
        var dependency = new ModDependency
        {
            ModVersion = CreateVersion("1.0.0", locked: true),
            Locked = false
        };

        Assert.Equal(new ProfileModPin(_modId, new ModVersionId("1.0.0"), false), dependency.ToPin());
    }


    private static ModVersion CreateVersion(string versionId, bool locked) => new()
    {
        RepoId = _repoId,
        ModId = _modId,
        Id = new ModVersionId(versionId),
        SequenceNumber = 0,
        DisplayName = versionId,
        Description = "",
        ContentHash = versionId,
        Locked = locked,
        Attributes = [],
        Created = default,
        Updated = default
    };
}
