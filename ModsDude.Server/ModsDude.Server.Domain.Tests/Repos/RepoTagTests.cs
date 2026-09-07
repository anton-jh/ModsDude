using ModsDude.Server.Domain.Repos;

namespace ModsDude.Server.Domain.Tests.Repos;

public class RepoTagTests
{
    [Fact]
    public void A_tag_is_four_digits()
    {
        var tag = RepoTag.For(new RepoId(Guid.NewGuid()));

        Assert.Equal(4, tag.Length);
        Assert.All(tag, x => Assert.True(char.IsAsciiDigit(x)));
    }

    [Fact]
    public void The_same_repo_always_gets_the_same_tag()
    {
        var repoId = new RepoId(Guid.NewGuid());

        Assert.Equal(RepoTag.For(repoId), RepoTag.For(repoId));
    }

    [Fact]
    public void Different_repos_get_different_tags()
    {
        // Four digits collide once in ten thousand, so this asserts on a spread rather than on every
        // pair being distinct: what matters is that the tag follows the id and not the order two
        // repos of the same name happened to be created in.
        var tags = Enumerable.Range(0, 100)
            .Select(_ => RepoTag.For(new RepoId(Guid.NewGuid())))
            .Distinct()
            .Count();

        Assert.True(tags > 95, $"Expected close to 100 distinct tags, got {tags}");
    }

    [Fact]
    public void A_tag_does_not_follow_the_name()
    {
        // The point of deriving it from the id: renaming a repo to get away from a clash would
        // otherwise change the very thing that resolved the clash.
        var repoId = new RepoId(Guid.NewGuid());

        var before = new Repo(new RepoName("Vanilla"), DateTime.UtcNow, new("someone"))
        {
            Id = repoId,
            AdapterData = new(new("adapter"), new("{}"))
        };

        var tag = RepoTag.For(before.Id);

        before.Name = new RepoName("Vanilla, but ours");

        Assert.Equal(tag, RepoTag.For(before.Id));
    }
}
