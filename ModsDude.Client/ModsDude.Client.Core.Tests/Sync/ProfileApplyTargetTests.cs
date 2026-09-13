using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class ProfileApplyTargetTests
{
    private readonly static Guid _repoId = Guid.NewGuid();
    private readonly static Guid _profileId = Guid.NewGuid();

    private readonly static GameIdentity _fs25 = new("farmingSimulator", "fs25");
    private readonly static GameIdentity _fs22 = new("farmingSimulator", "fs22");


    [Fact]
    public void The_target_is_the_game_already_on_that_profile()
    {
        var mine = Game(_fs25, new ActiveProfile(_repoId, _profileId));

        Assert.Same(mine, ProfileApplyTarget.Find([mine], _fs25, new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void A_game_on_another_profile_is_not_a_target()
    {
        var elsewhere = Game(_fs25, new ActiveProfile(_repoId, Guid.NewGuid()));

        Assert.Null(ProfileApplyTarget.Find([elsewhere], _fs25, new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void A_game_following_nothing_is_not_a_target()
    {
        Assert.Null(ProfileApplyTarget.Find([Game(_fs25, null)], _fs25, new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void A_drifted_game_is_the_target_by_definition_and_needs_no_pre_selection()
    {
        // Drift is a folder no longer matching its own active profile, so nothing about it changes
        // which game a re-apply targets.
        var drifted = Game(_fs25, new ActiveProfile(_repoId, _profileId));

        Assert.Same(drifted, ProfileApplyTarget.Find([drifted], _fs25, new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void The_same_profile_id_in_another_repo_is_not_a_target()
    {
        var elsewhere = Game(_fs25, new ActiveProfile(Guid.NewGuid(), _profileId));

        Assert.Null(ProfileApplyTarget.Find([elsewhere], _fs25, new ActiveProfile(_repoId, _profileId)));
    }

    /// <summary>
    /// <b>The search that used to be here.</b> Another game of the same adapter - FS22 beside FS25 -
    /// can be following a profile with this id and is not this repo's game, so the answer is keyed on
    /// which game the repo is about rather than on which games happen to name the profile.
    /// </summary>
    [Fact]
    public void Another_game_following_the_same_profile_is_not_this_repos_target()
    {
        var active = new ActiveProfile(_repoId, _profileId);
        var wrongGame = Game(_fs22, active);
        var rightGame = Game(_fs25, active);

        Assert.Same(rightGame, ProfileApplyTarget.Find([wrongGame, rightGame], _fs25, active));
    }

    [Theory]
    // The game is never named: there is one, and naming it would read as though there were a choice.
    // Nothing to apply to is the onboarding case, and the button says what it will actually do.
    [InlineData(true, "Save and apply")]
    [InlineData(false, "Save changes")]
    public void The_button_says_whether_anything_is_applied(bool hasTarget, string expected)
    {
        Assert.Equal(expected, ProfileApplyTarget.DescribeSaveAction(hasTarget));
    }


    private static Game Game(GameIdentity identity, ActiveProfile? activeProfile)
    {
        return new Game(identity, new PersistedGame
        {
            GameAdapterId = new GameAdapterId(identity.AdapterId, 1),
            Name = "Farming Simulator",
            AdapterLocalSettings = "{}",
            Targets = [new PersistedModTarget(new TargetKey("mods"), @$"C:\mods\{identity.Discriminator}")],
            ActiveProfile = activeProfile
        });
    }
}
