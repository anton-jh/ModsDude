using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Repos;

namespace ModsDude.Client.Core.Tests.Repos;

public class RepoListChangesTests
{
    private const string _settings = """{"game":"fs25"}""";


    [Fact]
    public void The_same_list_is_no_change()
    {
        var entry = Entry("Vanilla");

        Assert.Null(RepoListChanges.Between([entry], [Remote(entry)]));
    }

    [Fact]
    public void A_repo_only_the_server_has_was_added_and_one_only_this_machine_has_was_removed()
    {
        var kept = Entry("Vanilla");
        var gone = Entry("Old farm");
        var added = Remote(Entry("New farm"));

        var changes = RepoListChanges.Between([kept, gone], [Remote(kept), added]);

        Assert.Equal(["'New farm' was added.", "'Old farm' was removed."], changes!.Lines);
    }

    [Fact]
    public void A_rename_names_both_the_old_name_and_the_new()
    {
        var entry = Entry("Vanilla");

        var changes = RepoListChanges.Between([entry], [Remote(entry with { Name = "Modded" })]);

        Assert.Equal(["'Vanilla' was renamed to 'Modded'."], changes!.Lines);
    }

    [Fact]
    public void A_new_membership_level_is_a_change()
    {
        var entry = Entry("Vanilla");

        var changes = RepoListChanges.Between([entry], [Remote(entry with { MembershipLevel = RepoMembershipLevel.Admin })]);

        Assert.Equal(["You are now an Admin in 'Vanilla'."], changes!.Lines);
    }

    [Fact]
    public void Saved_game_settings_are_a_change()
    {
        var entry = Entry("Vanilla");

        var changes = RepoListChanges.Between([entry], [Remote(entry with { AdapterConfiguration = """{"game":"fs22"}""" })]);

        Assert.Equal(["'Vanilla' had its game settings changed."], changes!.Lines);
    }


    private static RepoListEntry Entry(string name)
    {
        return new RepoListEntry(Guid.NewGuid(), name, RepoMembershipLevel.Member, _settings);
    }

    private static RepoMembershipDto Remote(RepoListEntry entry)
    {
        return new RepoMembershipDto
        {
            Repo = new RepoDto
            {
                Id = entry.Id,
                Name = entry.Name,
                Tag = "0001",
                AdapterId = "fs",
                AdapterConfiguration = entry.AdapterConfiguration
            },
            MembershipLevel = entry.MembershipLevel
        };
    }
}
