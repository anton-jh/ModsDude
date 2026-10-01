namespace ModsDude.Client.Core.Savegames;

public interface ISavegameClaimWatch
{
    /// <summary>
    /// Reads the lists and records what they say.
    /// </summary>
    /// <returns>
    /// Whether anything the drift check reads about a held save - its head or its claim - changed, so
    /// the caller knows whether the notice's answer is now stale.
    /// </returns>
    Task<bool> RefreshAsync(CancellationToken ct);
}
