using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Savegames;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// The mod-list steps of a check-out: before it, where the mod folder is not on the savegame's
/// revision, and after it.
/// </summary>
public interface ISavegameProfileActivation
{
    /// <summary>
    /// Where the mod folder is not on the revision this savegame runs on, asks to activate its profile
    /// and does. A copy may decline and go ahead; a check-out may not.
    /// </summary>
    /// <param name="changed">Called after an activation, whether or not it succeeded.</param>
    /// <returns>
    /// Whether to carry on: nothing needed doing, the activation finished, or a copy was told to leave
    /// the folder alone.
    /// </returns>
    Task<bool> ConfirmActivateFirstAsync(
        Repo repo,
        Game game,
        SavegameDto savegame,
        SavegameCheckOutMode mode,
        Func<Task> changed,
        CancellationToken cancellationToken);

    /// <summary>
    /// Activates the profile a just checked-out savegame follows, and offers the mod list where the
    /// user declined because of mods the repo does not have.
    /// </summary>
    Task ActivateCheckedOutAsync(Repo repo, Game game, SavegameDto savegame, CancellationToken cancellationToken);
}
