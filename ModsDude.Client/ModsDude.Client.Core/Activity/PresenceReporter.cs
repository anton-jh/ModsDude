using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Connectivity;
using ModsDude.Client.Core.GameAdapters;
using ModsDude.Client.Core.GameProcesses;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Sync;

namespace ModsDude.Client.Core.Activity;

/// <summary>
/// Lets friends see that a game here is being played, so the one they are about to join is at the
/// top of their lists.
/// </summary>
/// <remarks>
/// <para>
/// <b>Only a game on a profile.</b> One on nothing has no row on the server, and nothing a friend
/// could join.
/// </para>
/// <para>
/// <b>A failed report is tried again on the next call.</b> A heartbeat that did not land is not
/// written down as sent, and neither is a stop - so a game that closed while the server was away
/// still stops once it is back, rather than looking played until it times out.
/// </para>
/// </remarks>
public sealed class PresenceReporter(
    IActivityClient activityClient,
    IGameRunningMonitor runningGames,
    IDriftCandidateSource games,
    IServerConnection connection,
    TimeProvider time,
    ILogger<PresenceReporter> logger)
    : IPresenceReporter
{
    /// <summary>Well inside the server's timeout, so one missed beat does not end the session.</summary>
    public static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(1);

    private readonly Lock _lock = new();

    /// <summary>The games told about as playing, with when the last heartbeat landed.</summary>
    private readonly Dictionary<GameIdentity, DateTimeOffset> _beating = [];


    public async Task ReportAsync(CancellationToken cancellationToken)
    {
        if (connection.IsOnline is false)
        {
            return;
        }

        var playing = games.GetDriftCandidates()
            .Where(x => x.ActiveProfile is not null && runningGames.IsRunning(x.Identity))
            .Select(x => x.Identity)
            .OrderBy(x => x.ToString(), StringComparer.Ordinal)
            .ToList();

        Dictionary<GameIdentity, DateTimeOffset> beating;

        lock (_lock)
        {
            beating = new(_beating);
        }

        var now = time.GetUtcNow();

        foreach (var game in playing.Where(x => beating.TryGetValue(x, out var last) is false || now - last >= HeartbeatInterval))
        {
            if (await SendAsync(game, isPlaying: true, cancellationToken))
            {
                lock (_lock)
                {
                    _beating[game] = now;
                }
            }
        }

        foreach (var game in beating.Keys.Except(playing).OrderBy(x => x.ToString(), StringComparer.Ordinal))
        {
            if (await SendAsync(game, isPlaying: false, cancellationToken))
            {
                lock (_lock)
                {
                    _beating.Remove(game);
                }
            }
        }
    }

    public void ClearUserState()
    {
        lock (_lock)
        {
            _beating.Clear();
        }
    }


    private async Task<bool> SendAsync(GameIdentity game, bool isPlaying, CancellationToken cancellationToken)
    {
        try
        {
            await activityClient.ReportPlayingV1Async(new ReportPlayingRequest { Game = game.ToString(), IsPlaying = isPlaying }, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogInformation(exception, "Could not tell the server whether {Game} is being played (playing: {IsPlaying}).", game, isPlaying);

            return false;
        }
    }
}
