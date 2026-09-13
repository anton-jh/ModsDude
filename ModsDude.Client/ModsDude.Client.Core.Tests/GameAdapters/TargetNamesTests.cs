using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;

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


    /// <summary>
    /// The names come off the adapter, which is why nothing writes them down: the settings are the
    /// only thing that decides them and they are re-read every time.
    /// </summary>
    [Fact]
    public void Read_asks_the_adapter_what_each_folder_is_called()
    {
        var names = TargetNames.Read(GameWith(Everything()), Adapter);

        Assert.Equal("Dedicated server", names[FakeMultiTargetSettings.Server]);
        Assert.Equal("MP client", names[FakeMultiTargetSettings.Client]);
        Assert.Equal("Singleplayer", names[FakeMultiTargetSettings.Solo]);
    }

    /// <summary>
    /// A folder the adapter named nothing is absent rather than present-and-blank, so the caller's
    /// lookup misses and <see cref="TargetNames.Of"/> falls back to the key. Farming Simulator's one
    /// target is exactly this.
    /// </summary>
    [Fact]
    public void Read_leaves_out_a_folder_the_adapter_named_nothing()
    {
        var unnamed = new FakeMultiTargetSettings { ServerModFolder = @"C:\server\mods" };

        // The fake names all three, so this asserts the shape rather than the fake: what a caller
        // does with a key that is not in the answer.
        Assert.Equal("solo", TargetNames.Of(FakeMultiTargetSettings.Solo,
            TargetNames.Read(GameWith(unnamed), Adapter).GetValueOrDefault(FakeMultiTargetSettings.Solo)));
    }

    /// <summary>
    /// <b>Settings this adapter version cannot read cost the labels, not the list.</b> Every caller
    /// iterates the persisted targets and looks names up in this, so an empty answer degrades to
    /// keys rather than to a folder going missing from a page.
    /// </summary>
    [Fact]
    public void Read_answers_empty_where_the_settings_cannot_be_hydrated()
    {
        var game = new Game(Identity, new PersistedGame
        {
            GameAdapterId = new GameAdapterId(Identity.AdapterId, 1),
            Name = "Multi-target test game",
            AdapterLocalSettings = "not json",
            Targets = [new PersistedModTarget(FakeMultiTargetSettings.Server, @"C:\server\mods")]
        });

        Assert.Empty(TargetNames.Read(game, Adapter));
    }


    private static GameIdentity Identity { get; } = new("_fake_multi_target");

    private static IBaseGameAdapter Adapter { get; } =
        new FakeMultiTargetGameAdapter().WithBaseSettings(new EmptyAdapterSettings());

    private static Game GameWith(FakeMultiTargetSettings settings)
        => new(Identity, new PersistedGame
        {
            GameAdapterId = new GameAdapterId(Identity.AdapterId, 1),
            Name = "Multi-target test game",
            AdapterLocalSettings = settings.Serialize(),
            Targets = [.. settings.Targets
                .Where(x => x.ModFolder is not null)
                .Select(x => new PersistedModTarget(x.Key, x.ModFolder!))]
        });

    private static FakeMultiTargetSettings Everything() => new()
    {
        ServerModFolder = @"C:\server\mods",
        ServerSavegameFolder = @"C:\server\saves",
        ClientModFolder = @"C:\client\mods",
        ClientSavegameFolder = @"C:\client\saves",
        SoloModFolder = @"C:\solo\mods",
        SoloSavegameFolder = @"C:\solo\saves"
    };
}
