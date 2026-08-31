using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class ProfileApplyTargetsTests
{
    private readonly static Guid _repoId = Guid.NewGuid();
    private readonly static Guid _profileId = Guid.NewGuid();


    [Fact]
    public void The_targets_are_the_instances_already_on_that_profile()
    {
        var mine = Instance("Farming Simulator 25", new ActiveProfile(_repoId, _profileId));
        var other = Instance("Dedicated server", new ActiveProfile(_repoId, Guid.NewGuid()));
        var none = Instance("Fresh install", null);

        var targets = ProfileApplyTargets.Derive([mine, other, none], new ActiveProfile(_repoId, _profileId));

        Assert.Equal([mine], targets);
    }

    [Fact]
    public void A_drifted_instance_is_in_the_set_by_definition_and_needs_no_pre_selection()
    {
        // Drift is a folder no longer matching its own active profile, so nothing about it changes
        // which instances a re-apply targets.
        var drifted = Instance("Farming Simulator 25", new ActiveProfile(_repoId, _profileId));

        Assert.Equal([drifted], ProfileApplyTargets.Derive([drifted], new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void The_same_profile_id_in_another_repo_is_not_a_target()
    {
        var elsewhere = Instance("Farming Simulator 25", new ActiveProfile(Guid.NewGuid(), _profileId));

        Assert.Empty(ProfileApplyTargets.Derive([elsewhere], new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void Several_instances_on_one_profile_are_all_targets()
    {
        var active = new ActiveProfile(_repoId, _profileId);
        var singleplayer = Instance("Singleplayer", active);
        var server = Instance("Dedicated server", active);

        Assert.Equal([singleplayer, server], ProfileApplyTargets.Derive([singleplayer, server], active));
    }

    [Theory]
    // One instance shows nothing at all - the word "instance" never appears, which is the common
    // case for most games. Zero drops the apply entirely.
    [InlineData(0, "Save changes")]
    [InlineData(1, "Save and apply")]
    [InlineData(3, "Save and apply to 3 instances")]
    public void The_button_scales_with_the_count(int count, string expected)
    {
        Assert.Equal(expected, ProfileApplyTargets.DescribeSaveAction(count));
    }


    private static LocalInstance Instance(string name, ActiveProfile? activeProfile)
    {
        var adapterId = new GameAdapterId("farmingSimulator", 1);

        return new LocalInstance(new PersistedLocalInstance
        {
            Id = Guid.NewGuid(),
            Scope = new InstanceScope(adapterId.ToString(), "fs25"),
            GameAdapterId = adapterId,
            Name = name,
            AdapterInstanceSettings = "{}",
            ModFolder = $@"C:\mods\{name}",
            ActiveProfile = activeProfile
        });
    }
}
