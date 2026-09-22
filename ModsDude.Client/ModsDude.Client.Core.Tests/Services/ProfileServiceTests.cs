using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Tests.Services;

public class ProfileServiceTests
{
    private static readonly Guid _repoId = Guid.NewGuid();
    private static readonly Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_saved_revision_becomes_the_head_the_drift_check_compares_against()
    {
        var service = ServiceWithProfileAtRevision(4);
        var active = new ActiveProfile(_repoId, _profileId);

        // The bug this pins: a save applies revision 5 and checks drift straight away, and against a
        // cached head of 4 the folder it just made reads as drifted until something else refreshes.
        service.NoteRevisionSaved(_profileId, 5);

        Assert.Equal(5, service.GetHeadRevision(active));
    }

    [Fact]
    public void Announcing_a_saved_revision_raises_the_update_once_and_only_when_it_changed()
    {
        var service = ServiceWithProfileAtRevision(4);
        var raised = new List<Guid>();

        service.ProfileUpdated += raised.Add;

        service.NoteRevisionSaved(_profileId, 5);
        service.NoteRevisionSaved(_profileId, 5);

        Assert.Equal([_profileId], raised);
    }

    [Fact]
    public void A_revision_saved_for_a_profile_this_client_does_not_hold_is_ignored()
    {
        var service = ServiceWithProfileAtRevision(4);
        var raised = false;

        service.ProfileUpdated += _ => raised = true;

        service.NoteRevisionSaved(Guid.NewGuid(), 5);

        Assert.False(raised);
        Assert.Equal(4, service.GetHeadRevision(new ActiveProfile(_repoId, _profileId)));
    }


    [Fact]
    public async Task A_check_before_any_refresh_asks_nothing()
    {
        var server = new FakeProfilesServer();
        var service = new ProfileService(server, null!, null!);

        await service.CheckForChanges(CancellationToken.None);

        Assert.Equal(0, server.Reads);
        Assert.Null(service.PendingChanges);
    }

    [Fact]
    public async Task A_check_records_what_changed_and_leaves_the_list_alone()
    {
        var server = new FakeProfilesServer();
        var service = new ProfileService(server, null!, null!);
        server.Profiles.Add(Profile("Season 4", 3));
        await service.RefreshProfiles(_repoId, CancellationToken.None);

        server.Profiles[0] = server.Profiles[0] with { HeadRevision = 4 };
        server.Profiles.Add(Profile("Season 5", 1));

        var raised = 0;
        service.PendingChangesChanged += (_, _) => raised++;

        await service.CheckForChanges(CancellationToken.None);

        Assert.Equal(["'Season 4' has a new revision.", "'Season 5' was added."], service.PendingChanges!.Lines);
        Assert.Equal(1, raised);
        Assert.Equal(["Season 4"], service.Profiles.Select(x => x.Name));
        Assert.Equal(3, service.Profiles[0].HeadRevision);
    }

    [Fact]
    public async Task A_refresh_brings_the_changes_in_and_clears_them()
    {
        var server = new FakeProfilesServer();
        var service = new ProfileService(server, null!, null!);
        await service.RefreshProfiles(_repoId, CancellationToken.None);

        server.Profiles.Add(Profile("Season 5", 1));
        await service.CheckForChanges(CancellationToken.None);

        await service.RefreshProfiles(_repoId, CancellationToken.None);

        Assert.Null(service.PendingChanges);
        Assert.Equal(["Season 5"], service.Profiles.Select(x => x.Name));
    }

    [Fact]
    public async Task A_check_that_finds_nothing_any_more_clears_what_an_earlier_one_found()
    {
        var server = new FakeProfilesServer();
        var service = new ProfileService(server, null!, null!);
        await service.RefreshProfiles(_repoId, CancellationToken.None);

        server.Profiles.Add(Profile("Season 5", 1));
        await service.CheckForChanges(CancellationToken.None);

        server.Profiles.Clear();
        await service.CheckForChanges(CancellationToken.None);

        Assert.Null(service.PendingChanges);
    }

    [Fact]
    public async Task A_check_is_thrown_away_when_this_machine_changed_the_list_while_it_was_out()
    {
        var server = new FakeProfilesServer();
        var service = new ProfileService(server, null!, null!);
        var season = Profile("Season 4", 3);
        server.Profiles.Add(season);
        await service.RefreshProfiles(_repoId, CancellationToken.None);

        // A save lands on this machine between the request and the answer. The answer is from before
        // it, so it would report this client's own revision as somebody else's - backwards.
        server.DuringRead = () => service.NoteRevisionSaved(season.Id, 4);

        await service.CheckForChanges(CancellationToken.None);

        Assert.Null(service.PendingChanges);
    }


    private static ProfileDto Profile(string name, int head)
    {
        return new ProfileDto
        {
            Id = Guid.NewGuid(),
            RepoId = _repoId,
            Name = name,
            HeadRevision = head
        };
    }

    /// <summary>
    /// No clients: nothing exercised here goes to the server, which is what makes this a test of the
    /// cache and not of the API.
    /// </summary>
    private static ProfileService ServiceWithProfileAtRevision(int head)
    {
        var service = new ProfileService(null!, null!, null!);

        service.Profiles.Add(new ProfileDto
        {
            Id = _profileId,
            RepoId = _repoId,
            Name = "Season 4",
            HeadRevision = head
        });

        return service;
    }
}
