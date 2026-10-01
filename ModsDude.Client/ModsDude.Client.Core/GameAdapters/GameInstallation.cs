using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Exceptions;
using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Core.GameAdapters;

public enum GameFolderKind
{
    Mods,
    Savegames
}

/// <param name="Name">
/// The target's name where the game reaches more than one folder of this kind, otherwise null.
/// </param>
public sealed record GameFolder(GameFolderKind Kind, TargetKey Key, string? Name, string Path);

/// <summary>
/// A connected game as this machine has it: every folder it reaches, or why it cannot be read right
/// now.
/// </summary>
/// <param name="Folders">
/// Mod folders first, in the order they are recorded, then savegame folders in the adapter's order.
/// Where the game cannot be read, only the recorded mod folders.
/// </param>
/// <param name="Problem">What went wrong reading the game, in a few words, or null where nothing did.</param>
public sealed record GameInstallation(IReadOnlyList<GameFolder> Folders, string? Problem)
{
    public bool IsFound => Problem is null;


    public static GameInstallation Read(Game game, IBaseGameAdapter baseAdapter, ILogger logger)
    {
        try
        {
            var adapter = game.GetAdapter(baseAdapter);

            return new([.. ModFolders(game, adapter), .. SavegameFolders(adapter)], null);
        }
        catch (UserFriendlyException exception)
        {
            logger.LogInformation("Could not read game {Game} on this machine: {Reason}", game.Identity, exception.DeveloperMessage);

            return new([.. RecordedModFolders(game)], exception.UserMessage);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not read the settings of game {Game}.", game.Identity);

            return new([.. RecordedModFolders(game)], "Could not read this game's settings");
        }
    }


    private static IEnumerable<GameFolder> ModFolders(Game game, ILocalGameAdapter adapter)
    {
        var names = adapter.GetLocalCapabilityAdapterFactory<ILocalModAdapter>()?.Invoke().ModTargets
            .ToDictionary(x => x.Key, x => x.DisplayName)
            ?? [];

        return game.Targets.Select(target => new GameFolder(
            GameFolderKind.Mods,
            target.Key,
            TargetNames.Distinguishing(target.Key, names.GetValueOrDefault(target.Key), game.Targets.Count),
            target.ModFolder));
    }

    private static IEnumerable<GameFolder> SavegameFolders(ILocalGameAdapter adapter)
    {
        var targets = adapter.GetLocalCapabilityAdapterFactory<ILocalSavegameAdapter>()?.Invoke().SavegameTargets
            ?? SavegameTargets.None;

        return targets.Select(target => new GameFolder(
            GameFolderKind.Savegames,
            target.Key,
            TargetNames.Distinguishing(target.Key, target.DisplayName, targets.Count),
            target.Path));
    }

    private static IEnumerable<GameFolder> RecordedModFolders(Game game)
    {
        return game.Targets.Select(target => new GameFolder(
            GameFolderKind.Mods,
            target.Key,
            TargetNames.Distinguishing(target.Key, null, game.Targets.Count),
            target.ModFolder));
    }
}
