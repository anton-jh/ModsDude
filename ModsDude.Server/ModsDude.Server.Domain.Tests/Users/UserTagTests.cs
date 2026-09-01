using ModsDude.Server.Domain.Users;

namespace ModsDude.Server.Domain.Tests.Users;

public class UserTagTests
{
    [Fact]
    public void A_tag_is_four_digits()
    {
        var tag = UserTag.For(new UserId("some-subject-id"));

        Assert.Equal(4, tag.Length);
        Assert.All(tag, x => Assert.True(char.IsAsciiDigit(x)));
    }

    [Fact]
    public void The_same_subject_always_gets_the_same_tag()
    {
        Assert.Equal(
            UserTag.For(new UserId("some-subject-id")),
            UserTag.For(new UserId("some-subject-id")));
    }

    [Fact]
    public void Different_subjects_get_different_tags()
    {
        // Four digits collide once in ten thousand, so this asserts on a spread rather than on every
        // pair being distinct: what matters is that the tag follows the subject and not the order.
        var tags = Enumerable.Range(0, 100)
            .Select(x => UserTag.For(new UserId($"subject-{x}")))
            .Distinct()
            .Count();

        Assert.True(tags > 95, $"Expected close to 100 distinct tags, got {tags}");
    }

    [Fact]
    public void A_display_name_falls_back_when_the_claim_says_nothing()
    {
        Assert.Equal(new DisplayName(DisplayName.Fallback), DisplayName.FromClaim(null));
        Assert.Equal(new DisplayName(DisplayName.Fallback), DisplayName.FromClaim("   "));
    }

    [Fact]
    public void A_display_name_is_stored_as_the_claim_says_it()
    {
        Assert.Equal(new DisplayName("Anton"), DisplayName.FromClaim("  Anton  "));
    }
}
