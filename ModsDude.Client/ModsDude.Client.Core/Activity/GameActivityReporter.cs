using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.ModsDudeServer.Generated;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// Tells the server which profile a game on this machine is now on, so the people this user shares
/// repos with can see it.
/// </summary>
/// <remarks>
/// <para>
/// <b>Never waited on, and never a reason for anything to fail.</b> The activation has already
/// happened here; the report only lets friends see it. A failure is logged and dropped rather than
/// queued - the next activation or re-apply says the same thing again, and a queue replaying a
/// switch from yesterday would announce it to everybody as news.
/// </para>
/// <para>
/// <b>What the game is on, not what the gesture was.</b> The revision reported is the one the game
/// is held to - a past savegame, or a friend being followed onto one - and null where it follows head,
/// which is what lets a friend follow the profile without pinning themselves to whatever number head
/// happened to be.
/// </para>
/// </remarks>
public class GameActivityReporter(
    IActivityClient activityClient,
    ILogger<GameActivityReporter> logger)
{
    /// <param name="pinnedRevision">The revision the game is held on, or null where it follows head.</param>
    /// <param name="savegameId">The savegame checked out, where <paramref name="kind"/> is a check-out.</param>
    public virtual void Report(
        GameIdentity game,
        Guid repoId,
        Guid profileId,
        int? pinnedRevision,
        GameActivityKind kind,
        Guid? savegameId = null)
    {
        var request = new RecordGameActivityRequest
        {
            Game = game.ToString(),
            RepoId = repoId,
            ProfileId = profileId,
            PinnedRevision = pinnedRevision,
            Kind = kind,
            SavegameId = savegameId
        };

        _ = SendAsync(() => activityClient.RecordGameActivityV1Async(request), game);
    }

    /// <summary>Says this game follows no profile any more, which takes it off friends' lists.</summary>
    public virtual void ReportCleared(GameIdentity game)
    {
        _ = SendAsync(() => activityClient.ClearGameActivityV1Async(game.ToString()), game);
    }


    private async Task SendAsync(Func<Task> send, GameIdentity game)
    {
        try
        {
            await send();
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not tell the server which profile '{Game}' is on.", game);
        }
    }
}
