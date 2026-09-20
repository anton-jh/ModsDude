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
