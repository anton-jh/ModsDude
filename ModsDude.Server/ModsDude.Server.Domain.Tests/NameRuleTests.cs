using ModsDude.Server.Domain.Exceptions;
using ModsDude.Server.Domain.Profiles;
using ModsDude.Server.Domain.Repos;
using ModsDude.Server.Domain.Savegames;

namespace ModsDude.Server.Domain.Tests;

public class NameRuleTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void An_empty_name_is_refused(string? name)
    {
        Assert.False(NameRule.IsValid(name));
    }

    [Fact]
    public void A_name_up_to_the_maximum_is_valid_and_longer_is_refused()
    {
        Assert.True(NameRule.IsValid(new string('x', NameRule.MaximumLength)));
        Assert.False(NameRule.IsValid(new string('x', NameRule.MaximumLength + 1)));
    }

    [Fact]
    public void Surrounding_whitespace_neither_counts_nor_is_kept()
    {
        Assert.True(NameRule.IsValid("  " + new string('x', NameRule.MaximumLength) + "  "));
        Assert.Equal("Season 4", new ProfileName("  Season 4 ").Value);
    }

    [Fact]
    public void Repo_profile_and_savegame_names_follow_the_rule()
    {
        Assert.Throws<DomainValidationException>(() => new RepoName(""));
        Assert.Throws<DomainValidationException>(() => new ProfileName(" "));
        Assert.Throws<DomainValidationException>(() => new SavegameName(new string('x', NameRule.MaximumLength + 1)));
    }
}
