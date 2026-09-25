using Microsoft.Extensions.Logging;
using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.Services;
using System.Windows.Threading;

namespace ModsDude.Client.Wpf.Services;

/// <summary>
/// Reads what friends are on now and then, which is how a switch or a check-out reaches the notice
/// column and a Windows toast.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whether or not the window is in sight</b>, for the reason <see cref="SavegameClaimWatcher"/>
/// gives: what it finds can become a toast, and the moment somebody most wants to hear that a friend
/// just started a savegame is when ModsDude is in the tray.
/// </para>
/// <para>
/// <b>Failures are logged and nothing else.</b> Nobody asked for this read, and the next tick asks again.
/// </para>
/// </remarks>
public sealed class FriendActivityWatcher(
    FriendActivityService friends,
    RepoRepository repoRepository,
    ILogger<FriendActivityWatcher> logger)
    : IDisposable
{
    /// <summary>One small list read, so this can be as frequent as the other watchers.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(3);

    /// <summary>Soon after start: what happened while the app was closed is what somebody opening it wants.</summary>
    private static readonly TimeSpan _firstLook = TimeSpan.FromSeconds(15);

    private DispatcherTimer? _timer;


    public void Start()
    {
        _timer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = _firstLook
        };

        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Dispose()
    {
        _timer?.Stop();
    }


    private void OnTick(object? sender, EventArgs e)
    {
        // Not signed in yet: nothing to ask with, so the first look waits for the next tick.
        if (repoRepository.HasLoaded is false)
        {
            return;
        }

        _timer!.Interval = Interval;

        _ = CheckAsync();
    }

    private async Task CheckAsync()
    {
        try
        {
            await friends.RefreshAsync(CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogInformation(exception, "Could not read which profiles friends are on.");
        }
    }
}
