using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Profiles;

namespace ModsDude.Client.Core.Tests.Profiles;

public class ProfileListChangesTests
{
    private static readonly Guid _repoId = Guid.NewGuid();


    [Fact]
    public void The_same_list_is_no_change()
    {
        var season = Profile("Season 4", 3);

        Assert.Null(ProfileListChanges.Between([season], [season with { }]));
    }

    [Fact]
    public void A_profile_only_the_server_has_was_added()
    {
        var changes = ProfileListChanges.Between([], [Profile("Season 4", 1)]);

        Assert.Equal(["'Season 4' was added."], changes!.Lines);
    }

    [Fact]
    public void A_profile_only_this_machine_has_was_removed()
    {
        var changes = ProfileListChanges.Between([Profile("Season 4", 1)], []);

        Assert.Equal(["'Season 4' was removed."], changes!.Lines);
    }

    [Fact]
    public void A_rename_names_both_the_old_name_and_the_new()
    {
        var local = Profile("Season 4", 1);

        var changes = ProfileListChanges.Between([local], [local with { Name = "Season 5" }]);

        Assert.Equal(["'Season 4' was renamed to 'Season 5'."], changes!.Lines);
    }

    [Fact]
    public void A_moved_head_counts_although_the_sidebar_does_not_draw_it()
    {
        var local = Profile("Season 4", 3);

        Assert.Equal(
            ["'Season 4' has a new revision."],
            ProfileListChanges.Between([local], [local with { HeadRevision = 4 }])!.Lines);

        Assert.Equal(
            ["'Season 4' has 3 new revisions."],
            ProfileListChanges.Between([local], [local with { HeadRevision = 6 }])!.Lines);
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
}
