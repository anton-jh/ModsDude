using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Tests;

/// <summary>
/// Restoring may rename, and a rename of something live is a rename, which checks the version it was
/// made against. So only an archived profile or savegame can be restored.
/// </summary>
public class RestoreTests
{
    private static readonly DateTime _now = new(2026, 1, 1);


    [Fact]
    public void A_live_profile_cannot_be_restored()
    {
        var profile = new Profile(new RepoId(Guid.NewGuid()), new ProfileName("Season 4"), _now, new ProfileCreateRequestId(Guid.NewGuid()));

        Assert.Throws<DomainValidationException>(() => profile.Restore(new ProfileName("Season 5")));
        Assert.Equal("Season 4", profile.Name.Value);
        Assert.Equal(0, profile.Version);
    }

    [Fact]
    public void A_live_savegame_cannot_be_restored()
    {
        var savegame = new Savegame(new RepoId(Guid.NewGuid()), new SavegameName("Harvest"), null, _now);

        Assert.Throws<DomainValidationException>(() => savegame.Restore(new SavegameName("Winter")));
        Assert.Equal("Harvest", savegame.Name.Value);
        Assert.Equal(0, savegame.Version);
    }

    [Fact]
    public void An_archived_profile_is_restored_under_a_new_name()
    {
        var profile = new Profile(new RepoId(Guid.NewGuid()), new ProfileName("Season 4"), _now, new ProfileCreateRequestId(Guid.NewGuid()));
        profile.Archive(_now);

        profile.Restore(new ProfileName("Season 4 (old)"));

        Assert.False(profile.IsArchived);
        Assert.Equal("Season 4 (old)", profile.Name.Value);
    }
}
