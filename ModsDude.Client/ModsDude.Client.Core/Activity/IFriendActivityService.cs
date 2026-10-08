using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Activity;

public interface IFriendActivityService : IUserScopedState
{
    /// <summary>
    /// Raised on the store's thread after every read that landed and when the user changes, and on a
    /// timer thread every <see cref="FriendActivityService.TickInterval"/>, since whether a game is
    /// being played depends on the time.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised with the news since the last time this fired. Once per change or start of play, never
    /// again for the same one.
    /// </summary>
    event EventHandler<IReadOnlyList<FriendNews>>? Announced;

    /// <summary>Every friend's game in the last week, most recently active first.</summary>
    IReadOnlyList<GameActivityDto> Rows { get; }

    /// <summary>Whether anything has been read yet, so a page can tell "nobody" from "not asked".</summary>
    bool HasLoaded { get; }

    /// <summary>The rows with news since this session began, as of now.</summary>
    IReadOnlyList<FriendNews> News { get; }

    /// <summary>
    /// Reads the list again. Calls that arrive while one is in flight share it.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
