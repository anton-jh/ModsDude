using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Wpf.Savegames;

/// <summary>
/// The mod-list steps around a savegame being taken: before it, where the mod folder is not on what
/// the savegame runs on, and after a check-out.
/// </summary>
public interface ISavegameProfileActivation
{
    /// <summary>
    /// Activates a profile the user has already agreed to as a step of something else, and says why
    /// where it did not finish.
    /// </summary>
    /// <param name="revision">
    /// The revision to install, or null for head. Named where nothing is holding the savegame yet,
    /// since the game would otherwise resolve head - wrong in compatibility mode.
    /// </param>
    /// <returns>Whether the folder is now on the profile, so the caller can go on.</returns>
    Task<bool> ActivateFirstAsync(
        Repo repo,
        Game game,
        Guid profileId,
        string profileName,
        int? revision,
        CancellationToken cancellationToken);

    /// <summary>
    /// Activates the profile a just checked-out savegame follows, and offers the mod list where the
    /// user declined because of mods the repo does not have.
    /// </summary>
    Task ActivateCheckedOutAsync(Repo repo, Game game, SavegameDto savegame, CancellationToken cancellationToken);
}
