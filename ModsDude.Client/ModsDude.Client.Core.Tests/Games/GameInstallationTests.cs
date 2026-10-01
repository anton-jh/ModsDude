using Microsoft.Extensions.Logging.Abstractions;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameAdapters.DynamicForms;
using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Persistence;
using ModsDude.Client.Core.Tests.GameAdapters;

namespace ModsDude.Client.Core.Tests.Games;

public class GameInstallationTests
{
    [Fact]
    public void A_game_with_one_target_lists_its_folders_unnamed()
    {
        var installation = Read(GameWith(new FakeMultiTargetSettings
        {
            SoloModFolder = @"C:\solo\mods",
            SoloSavegameFolder = @"C:\solo\saves"
        }));

        Assert.True(installation.IsFound);
        Assert.Equal(
            [
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Solo, null, @"C:\solo\mods"),
                new GameFolder(GameFolderKind.Savegames, FakeMultiTargetSettings.Solo, null, @"C:\solo\saves")
            ],
            installation.Folders);
    }

    [Fact]
    public void A_game_with_several_targets_lists_mods_then_savegames_each_named()
    {
        var installation = Read(GameWith(Everything()));

        Assert.Equal(
            [
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Server, "Dedicated server", @"C:\server\mods"),
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Client, "MP client", @"C:\client\mods"),
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Solo, "Singleplayer", @"C:\solo\mods"),
                new GameFolder(GameFolderKind.Savegames, FakeMultiTargetSettings.Server, "Dedicated server", @"C:\server\saves"),
                new GameFolder(GameFolderKind.Savegames, FakeMultiTargetSettings.Client, "MP client", @"C:\client\saves"),
                new GameFolder(GameFolderKind.Savegames, FakeMultiTargetSettings.Solo, "Singleplayer", @"C:\solo\saves")
            ],
            installation.Folders);
    }

    [Fact]
    public void Reading_twice_gives_the_same_folders_in_the_same_order()
    {
        var game = GameWith(Everything());

        Assert.Equal(Read(game).Folders, Read(game).Folders);
    }

    [Fact]
    public void A_game_with_no_savegame_folder_lists_only_its_mod_folders()
    {
        var installation = Read(GameWith(new FakeMultiTargetSettings
        {
            ServerModFolder = @"C:\server\mods",
            ClientModFolder = @"C:\client\mods"
        }));

        Assert.All(installation.Folders, x => Assert.Equal(GameFolderKind.Mods, x.Kind));
        Assert.Equal(2, installation.Folders.Count);
    }

    [Fact]
    public void A_game_whose_folder_is_gone_says_so_and_keeps_its_recorded_mod_folders()
    {
        var game = GameWith(Everything());

        var installation = GameInstallation.Read(game, new MissingGameAdapter(), NullLogger.Instance);

        Assert.False(installation.IsFound);
        Assert.Equal("Game folder not found", installation.Problem);
        Assert.Equal(
            [
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Server, "server", @"C:\server\mods"),
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Client, "client", @"C:\client\mods"),
                new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Solo, "solo", @"C:\solo\mods")
            ],
            installation.Folders);
    }

    [Fact]
    public void Settings_that_cannot_be_read_are_a_problem_rather_than_a_crash()
    {
        var game = new Game(Identity, new PersistedGame
        {
            GameAdapterId = new GameAdapterId(Identity.AdapterId, 1),
            Name = "Multi-target test game",
            AdapterLocalSettings = "not json",
            Targets = [new PersistedModTarget(FakeMultiTargetSettings.Solo, @"C:\solo\mods")]
        });

        var installation = Read(game);

        Assert.False(installation.IsFound);
        Assert.NotNull(installation.Problem);
        Assert.Equal([new GameFolder(GameFolderKind.Mods, FakeMultiTargetSettings.Solo, null, @"C:\solo\mods")], installation.Folders);
    }


    private static GameIdentity Identity { get; } = new("_fake_multi_target");

    private static IBaseGameAdapter Adapter { get; } =
        new FakeMultiTargetGameAdapter().WithBaseSettings(new EmptyAdapterSettings());

    private static GameInstallation Read(Game game) => GameInstallation.Read(game, Adapter, NullLogger.Instance);

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


    /// <summary>The fake, as a machine sees it once the game's data folder has been deleted.</summary>
    private sealed class MissingGameAdapter : FakeMultiTargetGameAdapter, IBaseGameAdapter
    {
        private readonly IBaseGameAdapter _inner = new FakeMultiTargetBaseGameAdapter();

        public DynamicForm BaseSettings => _inner.BaseSettings;
        public bool CanSupportMods => _inner.CanSupportMods;
        public bool CanSupportSavegames => _inner.CanSupportSavegames;

        public DynamicForm GetLocalSettingsTemplate() => _inner.GetLocalSettingsTemplate();
        public DynamicForm DeserializeLocalSettings(string serializedLocalSettings) => _inner.DeserializeLocalSettings(serializedLocalSettings);
        public Func<T>? GetBaseCapabilityAdapterFactory<T>() => _inner.GetBaseCapabilityAdapterFactory<T>();

        public ILocalGameAdapter WithLocalSettings(string serializedLocalSettings) => throw NotFound();
        public ILocalGameAdapter WithLocalSettings(DynamicForm localSettings) => throw NotFound();

        private static UserFriendlyException NotFound() => new("Game folder not found", "The data folder does not exist.");
    }
}
