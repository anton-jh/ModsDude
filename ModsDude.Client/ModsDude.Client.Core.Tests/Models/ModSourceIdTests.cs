using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.Tests.Models;

/// <summary>
/// What "do not look in this folder" is remembered against. It is persisted, so what counts as the
/// same source is what decides whether switching one folder off switches another off with it.
/// </summary>
public class ModSourceIdTests
{
    /// <summary>
    /// The reason a source is per target rather than per game. One id for the game would make the
    /// dedicated server's folder and the MP client's folder one source: scanning would look in one
    /// of them and report what the other holds as missing from the machine, and switching either off
    /// would switch both.
    /// </summary>
    [Fact]
    public void Two_folders_of_one_game_are_two_sources()
    {
        Assert.NotEqual(
            ModSourceId.ForTarget(Keys.Target("server")),
            ModSourceId.ForTarget(Keys.Target("client")));
    }

    /// <summary>And the same folder reached twice is the same source, which is what makes it remembered.</summary>
    [Fact]
    public void The_same_target_is_the_same_source()
    {
        Assert.Equal(
            ModSourceId.ForTarget(Keys.Target("server")),
            ModSourceId.ForTarget(Keys.Target("server")));
    }

    /// <summary>One key under two games is two folders, so the game has to be in the id too.</summary>
    [Fact]
    public void One_key_under_two_games_is_two_sources()
    {
        Assert.NotEqual(
            ModSourceId.ForTarget(Keys.Target(discriminator: "fs25")),
            ModSourceId.ForTarget(Keys.Target(discriminator: "fs22")));
    }
}
