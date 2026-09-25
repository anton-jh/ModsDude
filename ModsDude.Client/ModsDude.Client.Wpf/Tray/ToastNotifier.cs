using ModsDude.Client.Core.Activity;
using ModsDude.Client.Core.ModsDudeServer.Generated;
using ModsDude.Client.Core.Notices;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// Says the things the window says, to somebody who is not looking at the window.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two sources, one rule.</b> Drift notices from the column and the in-app toasts a finished action
/// puts up at the bottom of the window are both drawn to a window that, in the tray or behind the game,
/// nobody is looking at. Each becomes a Windows toast exactly when the window is not the thing in front
/// of the user - hidden, minimised, or simply not the active window - and never otherwise, so somebody
/// working in the app is not told twice.
/// </para>
/// <para>
/// <b>What a drift toast is worth is decided in Core</b> - see <see cref="DriftToastPlanner"/> - which
/// is why this is short: it asks the window, asks the planner, and hands the answer to Windows.
/// </para>
/// <para>
/// <b>Clicking only ever opens.</b> The window comes forward, and a toast about one drifted folder or
/// savegame goes on to the page that notice points at. It never applies anything: a toast is answered
/// from another window, often long after, and a button that changed a mod folder from there would be
/// doing it without looking at what it changes.
/// </para>
/// </remarks>
public sealed class ToastNotifier(
    MainWindow window,
    NoticeCenterViewModel notices,
    ToastCenterViewModel appToasts,
    INoticeEnvironment environment,
    ClientSettingsRepository settings,
    FriendActivityService friends,
    IFriendActivityEnvironment friendEnvironment,
    ISystemToasts system)
{
    private const string _driftGroup = "drift";
    private const string _appGroup = "app";
    private const string _friendsGroup = "friends";
    private const string _noticeArgument = "notice";
    private const string _openArgument = "action";

    private readonly DriftToastPlanner _planner = new();


    public void Start()
    {
        notices.Refreshed += OnNoticesRefreshed;
        appToasts.Announced += OnAppToast;
        friends.Announced += OnFriendNews;
        system.Activated += OnActivated;

        // Once somebody is looking at the window, what it told them while they were not is either on
        // screen or stale. Left in Action Center it would be a toast about drift they have already seen.
        //
        // Off the UI thread, and not for speed: Activated is raised while a minimised window is still
        // handling its activation, before it has been restored, and clearing is a blocking call into
        // another process. Made there, it swallowed the taskbar's restore - the first click on the
        // button only beeped, and it took a second to bring the window up.
        window.Activated += (_, _) => Task.Run(system.ClearAll);
    }


    private bool Enabled => settings.Settings.Background.Notifications;

    private bool WindowInFront => window.IsVisible
        && window.WindowState is not WindowState.Minimized
        && window.IsActive;


    private void OnNoticesRefreshed(object? sender, IReadOnlyList<Notice> live)
    {
        // Asked even when switched off, so that turning it on later does not announce everything that
        // has been true for a week as if it had just happened.
        var toast = _planner.Observe(live, environment.ReposLoaded, WindowInFront || Enabled is false);

        if (toast is null)
        {
            return;
        }

        system.Show(new SystemToast(
            toast.Title,
            toast.Body,
            _driftGroup,
            // One digest that each new one replaces: the newer toast lists everything the older did.
            Tag: "digest",
            toast.NoticeKey is string key
                ? new Dictionary<string, string> { [_noticeArgument] = key }
                : new Dictionary<string, string> { [_openArgument] = "open" }));
    }

    /// <summary>
    /// A toast the window drew, mirrored where somebody will see it. Raised on whichever thread asked.
    /// </summary>
    private void OnAppToast(string message, ViewModel.Services.ToastSeverity severity)
    {
        if (Enabled is false)
        {
            return;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (WindowInFront)
            {
                return;
            }

            system.Show(new SystemToast(
                message,
                Body: null,
                _appGroup,
                // The same words are the same toast: the window collapses a repeated card the same way.
                Tag: message.GetHashCode().ToString("x"),
                new Dictionary<string, string> { [_openArgument] = "open" }));
        });
    }

    /// <summary>
    /// A friend switched profile or checked out a savegame. One toast per friend per game, each
    /// replacing the last about the same one - what they are on now is the news.
    /// </summary>
    /// <remarks>
    /// Clicking opens the window on the column, where the card offers to follow them: the rule above
    /// holds here too, and a toast never changes a mod folder by itself.
    /// </remarks>
    private void OnFriendNews(object? sender, IReadOnlyList<GameActivityDto> news)
    {
        if (Enabled is false)
        {
            return;
        }

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            if (WindowInFront)
            {
                return;
            }

            foreach (var activity in news)
            {
                system.Show(new SystemToast(
                    FriendActivityRules.Headline(activity),
                    FriendActivityRules.Describe(activity, friendEnvironment),
                    _friendsGroup,
                    Tag: FriendActivityRules.NoticeKey(activity).GetHashCode().ToString("x"),
                    new Dictionary<string, string> { [_openArgument] = "open" }));
            }
        });
    }

    /// <summary>A click, from whichever thread Windows delivered it on.</summary>
    private void OnActivated(IReadOnlyDictionary<string, string> arguments)
    {
        Application.Current?.Dispatcher.InvokeAsync(async () =>
        {
            window.ShowFromTray();

            if (arguments.TryGetValue(_noticeArgument, out var key))
            {
                await notices.OpenAsync(key);
            }
        });
    }
}
