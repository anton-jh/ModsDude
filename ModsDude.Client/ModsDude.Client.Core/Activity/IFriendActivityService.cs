using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Services;

namespace ModsDude.Client.Core.Activity;

public interface IFriendActivityService : IUserScopedState
{
    /// <summary>Raised after every read that landed, on whichever thread it completed.</summary>
    event EventHandler? Changed;

    /// <summary>
    /// Raised with the rows that changed since the last time this fired. Once per change, never
    /// again for the same one.
    /// </summary>
    event EventHandler<IReadOnlyList<GameActivityDto>>? Announced;

    /// <summary>Every friend's game in the last week, most recently active first.</summary>
    IReadOnlyList<GameActivityDto> Rows { get; }

    /// <summary>Whether anything has been read yet, so a page can tell "nobody" from "not asked".</summary>
    bool HasLoaded { get; }

    /// <summary>The rows that changed since this session began.</summary>
    IReadOnlyList<GameActivityDto> News { get; }

    /// <summary>
    /// Reads the list again. Calls that arrive while one is in flight share it.
    /// </summary>
    Task RefreshAsync(CancellationToken cancellationToken);
}
