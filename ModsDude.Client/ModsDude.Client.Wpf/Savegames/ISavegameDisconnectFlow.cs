using ModsDude.Client.Core.Models;

namespace ModsDude.Client.Wpf.Savegames;

public interface ISavegameDisconnectFlow
{
    /// <summary>
    /// Stops tracking a hold this machine can no longer act on, after a confirmation naming what is
    /// lost. Nothing on disk changes and the server is not told.
    /// </summary>
    /// <param name="savegameName">What to call the save, where the caller can name it.</param>
    /// <returns>False where the modal was dismissed, or there was nothing to forget.</returns>
    Task<bool> DisconnectAsync(Game game, Guid savegameId, string? savegameName, SavegameDisconnectReason reason);
}


/// <summary>Why a hold can no longer be acted on, which decides what the confirmation says.</summary>
public abstract record SavegameDisconnectReason
{
    /// <summary>It sits in a folder the settings no longer name. The claim is kept.</summary>
    /// <param name="FolderName">The folder holding it, where the caller can name it.</param>
    public sealed record FolderNotNamed(string? FolderName) : SavegameDisconnectReason;

    /// <summary>Its repo is no longer one of this account's, so it can never be checked in.</summary>
    /// <param name="Played">Whether it has been played since it was checked out.</param>
    public sealed record RepoGone(bool Played) : SavegameDisconnectReason;
}
