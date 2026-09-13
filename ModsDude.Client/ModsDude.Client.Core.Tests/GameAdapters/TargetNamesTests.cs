using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Tests.GameAdapters;

/// <summary>
/// The one rule for naming a folder, which four surfaces used to spell three different ways.
/// </summary>
public class TargetNamesTests
{
    [Fact]
    public void Of_prefers_what_the_adapter_called_it()
    {
        Assert.Equal("MP client", TargetNames.Of(new TargetKey("mp"), "MP client"));
    }

    /// <summary>
    /// The case the drift notice and an unreachable hold both land in: nothing can be asked what the
    /// folder was called, and the key is a word an adapter author picked rather than a placeholder.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Of_falls_back_to_the_key_where_nothing_named_it(string? displayName)
    {
        Assert.Equal("mp", TargetNames.Of(new TargetKey("mp"), displayName));
    }

    /// <summary>
    /// Farming Simulator does not have a mod folder <em>called</em> something, so nothing about a
    /// game with one target ever mentions which folder it means.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Distinguishing_says_nothing_where_there_is_nothing_to_tell_apart(int targetCount)
    {
        Assert.Null(TargetNames.Distinguishing(new TargetKey("mods"), "Mods", targetCount));
    }

    [Fact]
    public void Distinguishing_names_the_folder_where_the_game_reaches_several()
    {
        Assert.Equal("MP client", TargetNames.Distinguishing(new TargetKey("mp"), "MP client", 3));
    }

    [Fact]
    public void Distinguishing_falls_back_to_the_key_too()
    {
        Assert.Equal("mp", TargetNames.Distinguishing(new TargetKey("mp"), null, 3));
    }
}
