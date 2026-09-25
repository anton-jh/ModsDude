using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class ProfileActivationTests
{
    private readonly static Guid _repoId = Guid.NewGuid();
    private readonly static Guid _profileId = Guid.NewGuid();


    [Fact]
    public void A_game_already_on_the_profile_is_being_re_applied()
    {
        var target = new ActiveProfile(_repoId, _profileId);

        Assert.Equal(ProfileActivationKind.Apply, ProfileActivation.Describe(target, target));
        Assert.Equal("Re-apply", ProfileActivation.Label(ProfileActivation.Describe(target, target)));
    }

    [Fact]
    public void A_game_on_another_profile_is_being_moved()
    {
        var current = new ActiveProfile(_repoId, Guid.NewGuid());
        var target = new ActiveProfile(_repoId, _profileId);

        Assert.Equal(ProfileActivationKind.Activate, ProfileActivation.Describe(current, target));
        Assert.Equal("Activate", ProfileActivation.Label(ProfileActivation.Describe(current, target)));
    }

    /// <summary>
    /// A game held on a past revision of this very profile - following a friend onto their savegame -
    /// still has somewhere to be moved: onto head. That is the one way back, so it is an activation.
    /// </summary>
    [Fact]
    public void A_game_pinned_to_the_profile_is_being_moved_to_head()
    {
        var target = new ActiveProfile(_repoId, _profileId);

        Assert.Equal(ProfileActivationKind.Activate, ProfileActivation.Describe(target, target, pinnedRevision: 4));
    }

    [Fact]
    public void A_game_on_nothing_is_being_moved_too()
    {
        Assert.Equal(
            ProfileActivationKind.Activate,
            ProfileActivation.Describe(null, new ActiveProfile(_repoId, _profileId)));
    }

    /// <summary>
    /// While a past savegame is held, the button's only remaining job is repairing folder drift back
    /// to that savegame's revision - applying the profile's latest is exactly what the apply table
    /// refuses - so it says which revision rather than implying the newest one.
    /// </summary>
    [Fact]
    public void A_pinned_revision_puts_its_number_on_the_re_apply()
    {
        var target = new ActiveProfile(_repoId, _profileId);
        var kind = ProfileActivation.Describe(target, target);

        Assert.Equal("Re-apply rev 4", ProfileActivation.Label(kind, 4));
        Assert.Equal("Re-apply", ProfileActivation.Label(kind, null));
    }

    /// <summary>
    /// Nothing pins the folder for a profile the game is not on - the hold is per profile - so the
    /// combination cannot arise, and the label stays the plain one either way.
    /// </summary>
    [Fact]
    public void Moving_an_game_never_names_a_revision()
    {
        var current = new ActiveProfile(_repoId, Guid.NewGuid());
        var kind = ProfileActivation.Describe(current, new ActiveProfile(_repoId, _profileId));

        Assert.Equal("Activate", ProfileActivation.Label(kind, 4));
    }

    [Fact]
    public void The_same_profile_id_in_another_repo_is_a_different_profile()
    {
        var current = new ActiveProfile(Guid.NewGuid(), _profileId);

        Assert.Equal(
            ProfileActivationKind.Activate,
            ProfileActivation.Describe(current, new ActiveProfile(_repoId, _profileId)));
    }
}
