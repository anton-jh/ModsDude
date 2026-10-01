using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Activity;

public interface IGameActivityReporter
{
    /// <param name="pinnedRevision">The revision the game is held on, or null where it follows head.</param>
    /// <param name="savegameId">The savegame checked out, where <paramref name="kind"/> is a check-out.</param>
    void Report(
        GameIdentity game,
        Guid repoId,
        Guid profileId,
        int? pinnedRevision,
        GameActivityKind kind,
        Guid? savegameId = null);

    /// <summary>Says this game follows no profile any more, which takes it off friends' lists.</summary>
    void ReportCleared(GameIdentity game);
}
