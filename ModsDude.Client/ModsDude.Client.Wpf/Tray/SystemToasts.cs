using System.IO;
using CommunityToolkit.WinUI.Notifications;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Windows.Data.Xml.Dom;
using Windows.UI.Notifications;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>One Windows notification, in the terms this app cares about.</summary>
/// <param name="Group">
/// What a replacement is looked for within. Together with <paramref name="Tag"/> it is how a newer
/// toast takes the place of an older one in Action Center instead of stacking beside it.
/// </param>
/// <param name="Arguments">Handed back untouched when the toast, or its button, is clicked.</param>
public sealed record SystemToast(
    string Title,
    string? Body,
    string Group,
    string? Tag,
    IReadOnlyDictionary<string, string> Arguments);


/// <summary>
/// Windows toast notifications, and nothing else about them.
/// </summary>
/// <remarks>
/// An interface so <see cref="ToastNotifier"/> - which is where every decision is - can be exercised
/// without a desktop to show them on.
/// </remarks>
public interface ISystemToasts
{
    /// <summary>Raised, off the UI thread, with the arguments of the toast (or its button) that was clicked.</summary>
    event Action<IReadOnlyDictionary<string, string>>? Activated;

    void Show(SystemToast toast);

    /// <summary>Takes back every toast this app has up, which are stale once somebody is looking at the window.</summary>
    void ClearAll();
}


/// <summary><see cref="ISystemToasts"/> over Windows' own notification API.</summary>
/// <remarks>
/// <para>
/// <b>Windows' API, with the toolkit only writing the XML.</b> The toolkit's own sender for unpackaged
/// apps registers a COM activator and an identity Windows then declines to show notifications for - see
/// <see cref="StartMenuShortcut"/>. This claims the identity itself instead: <see cref="Register"/> names
/// the process, records its display name and makes sure the Start Menu shortcut Windows needs is there.
/// </para>
/// <para>
/// <b>Clicks are answered by the running app, and there is no other case to answer.</b> A toast's
/// <c>Activated</c> event fires in the process that showed it, and the app takes its toasts back when it
/// exits - so nothing is left in Action Center to be clicked after there is nobody to click it on. One
/// that does outlive a crash starts the app from its shortcut, which is a fine thing for it to do.
/// </para>
/// <para>
/// <b>Every failure is absorbed.</b> Notifications can be switched off for the app or the machine, and
/// the shell can decline them for reasons of its own; a toast is a courtesy on top of a column that
/// already says the same thing, and failing to send one is no reason to disturb what is running.
/// </para>
/// </remarks>
public sealed class WindowsToasts(ILogger<WindowsToasts> logger) : ISystemToasts
{
    /// <summary>
    /// Long enough to still be there when somebody comes back to the machine after a night, short enough
    /// that a toast about drift they have long since dealt with does not sit in Action Center for a week.
    /// </summary>
    private static readonly TimeSpan _lifetime = TimeSpan.FromHours(12);

    /// <summary>
    /// The notifications still up. Held because Windows raises <c>Activated</c> on the object that was
    /// shown, and one that has been collected raises nothing.
    /// </summary>
    private readonly ConcurrentDictionary<Guid, ToastNotification> _live = [];

    private string? _appUserModelId;


    public event Action<IReadOnlyDictionary<string, string>>? Activated;


    /// <summary>
    /// Claims this process's identity for notifications. Before the first window, so the taskbar button
    /// and the shortcut agree on who this is.
    /// </summary>
    /// <returns>Whether toasts can be sent at all. False leaves every <see cref="Show"/> a no-op.</returns>
    public bool Register(string appUserModelId, string displayName, string? executablePath, bool ensureShortcut = true)
    {
        try
        {
            if (executablePath is not { Length: > 0 }
                || string.Equals(Path.GetFileNameWithoutExtension(executablePath), "dotnet", StringComparison.OrdinalIgnoreCase))
            {
                // Run under the dotnet host there is no executable to point a shortcut at, and one
                // pointing at dotnet.exe would attribute every .NET app on the machine to this one.
                logger.LogInformation("Windows notifications are off: this is not running from its own executable.");

                return false;
            }

            Marshal.ThrowExceptionForHR(SetCurrentProcessExplicitAppUserModelID(appUserModelId));

            using (var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\AppUserModelId\{appUserModelId}"))
            {
                key.SetValue("DisplayName", displayName);
            }

            // An installed copy has the installer's shortcut, made with this same identity, and rewriting it\n            // would replace what the installer put there - and removes with it on uninstall - with ours.\n            if (ensureShortcut)\n            {\n                StartMenuShortcut.Ensure(displayName, executablePath, appUserModelId);\n            }

            _appUserModelId = appUserModelId;

            return true;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not set up Windows notifications.");

            return false;
        }
    }

    public void Show(SystemToast toast)
    {
        if (_appUserModelId is not string appUserModelId)
        {
            return;
        }

        try
        {
            var builder = new ToastContentBuilder().AddText(toast.Title);

            if (toast.Body is { Length: > 0 } body)
            {
                builder.AddText(body);
            }

            // On the toast itself and again on the button: a button does not inherit them, and clicking
            // either has to come back as the same thing.
            var open = new ToastButton().SetContent("Open");

            foreach (var (key, value) in toast.Arguments)
            {
                builder.AddArgument(key, value);
                open.AddArgument(key, value);
            }

            builder.AddButton(open);

            var xml = new XmlDocument();
            xml.LoadXml(builder.GetToastContent().GetContent());

            var notification = new ToastNotification(xml)
            {
                Group = toast.Group,
                ExpirationTime = DateTimeOffset.Now + _lifetime
            };

            if (toast.Tag is not null)
            {
                notification.Tag = toast.Tag;
            }

            var id = Guid.NewGuid();

            notification.Activated += (_, e) => OnActivated(id, e);
            notification.Dismissed += (_, _) => _live.TryRemove(id, out _);
            notification.Failed += (_, e) => logger.LogWarning("Windows could not show a notification: {Error}.", e.ErrorCode);

            _live[id] = notification;

            ToastNotificationManager.CreateToastNotifier(appUserModelId).Show(notification);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not show a Windows notification.");
        }
    }

    public void ClearAll()
    {
        if (_appUserModelId is not string appUserModelId)
        {
            return;
        }

        try
        {
            ToastNotificationManager.History.Clear(appUserModelId);
            _live.Clear();
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not clear the Windows notifications.");
        }
    }


    private void OnActivated(Guid id, object e)
    {
        _live.TryRemove(id, out _);

        var arguments = e is ToastActivatedEventArgs { Arguments: { Length: > 0 } raw }
            ? ToastArguments.Parse(raw).ToDictionary(x => x.Key, x => x.Value)
            : [];

        Activated?.Invoke(arguments);
    }


    [DllImport("shell32.dll")]
    private static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string appId);
}
