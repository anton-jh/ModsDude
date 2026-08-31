using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Tests.Sync;

public class InstanceActivationTests
{
    private readonly static Guid _repoId = Guid.NewGuid();
    private readonly static Guid _profileId = Guid.NewGuid();


    [Fact]
    public void An_instance_already_on_the_profile_is_being_re_applied()
    {
        var target = new ActiveProfile(_repoId, _profileId);

        Assert.Equal(InstanceActivationKind.Reapply, InstanceActivation.Describe(target, target));
        Assert.Equal("Re-apply", InstanceActivation.Label(InstanceActivation.Describe(target, target)));
    }

    [Fact]
    public void An_instance_on_another_profile_is_being_moved()
    {
        var current = new ActiveProfile(_repoId, Guid.NewGuid());
        var target = new ActiveProfile(_repoId, _profileId);

        Assert.Equal(InstanceActivationKind.Activate, InstanceActivation.Describe(current, target));
        Assert.Equal("Activate", InstanceActivation.Label(InstanceActivation.Describe(current, target)));
    }

    [Fact]
    public void An_instance_on_nothing_is_being_moved_too()
    {
        Assert.Equal(
            InstanceActivationKind.Activate,
            InstanceActivation.Describe(null, new ActiveProfile(_repoId, _profileId)));
    }

    [Fact]
    public void The_same_profile_id_in_another_repo_is_a_different_profile()
    {
        var current = new ActiveProfile(Guid.NewGuid(), _profileId);

        Assert.Equal(
            InstanceActivationKind.Activate,
            InstanceActivation.Describe(current, new ActiveProfile(_repoId, _profileId)));
    }
}
