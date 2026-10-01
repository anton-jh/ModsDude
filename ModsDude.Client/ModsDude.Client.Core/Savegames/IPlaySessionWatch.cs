using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Core.Savegames;

public interface IPlaySessionWatch
{
    /// <summary>
    /// Raised by <see cref="PollAsync"/>, on the thread that called it, for a session that ended with
    /// at least one checked-out savegame played in it.
    /// </summary>
    event EventHandler<IReadOnlyList<PlayedSavegame>>? Played;

    /// <summary>Whether the game was running at the last poll.</summary>
    bool IsRunning(GameIdentity game);

    /// <summary>
    /// Looks at which games are running now, and reports the checked-out savegames played in every
    /// session that has ended since the last look.
    /// </summary>
    Task<IReadOnlyList<PlayedSavegame>> PollAsync(CancellationToken ct);
}
