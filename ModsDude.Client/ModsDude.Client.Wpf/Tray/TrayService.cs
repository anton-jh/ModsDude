using H.NotifyIcon;
using H.NotifyIcon.Core;
using Microsoft.Extensions.Logging;
using ModsDude.Client.Core;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.ViewModel.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// The notification-area icon, and what closing the window means while it is there.
/// </summary>
/// <remarks>
/// <para>
/// <b>Close-to-tray is only offered once the icon exists.</b> <see cref="MainWindow.HideOnClose"/> is
/// set at the end of <see cref="Start"/>, after the icon has been created. A window hidden with no
/// icon to bring it back would be an app nobody can reach, and creating the icon can fail - Explorer
/// is not up yet at logon, or the shell is not one that has a tray - so the failure has to land on
/// "closing quits", which is what the app has always done.
/// </para>
/// <para>
/// The status line is read off the notice column rather than kept as a second opinion: the tray says
/// what the rail says, from the same counts.
/// </para>
/// </remarks>
public sealed class TrayService(
    MainWindow window,
    NoticeCenterViewModel notices,
    ClientSettingsRepository settings,
    ILogger<TrayService> logger) : IDisposable
{
    private static string _name => AppIdentity.DisplayName;

    private TaskbarIcon? _icon;
    private MenuItem? _status;


    /// <returns>Whether the icon is up. False leaves the app behaving as if there were no tray.</returns>
    public bool Start()
    {
        try
        {
            _status = new MenuItem { IsEnabled = false };

            var open = new MenuItem { Header = $"Open {_name}" };
            open.Click += (_, _) => window.ShowFromTray();

            var quit = new MenuItem { Header = "Quit" };
            quit.Click += (_, _) => window.Quit();

            var menu = new ContextMenu();
            menu.Items.Add(open);
            menu.Items.Add(new Separator());
            menu.Items.Add(_status);
            menu.Items.Add(new Separator());
            menu.Items.Add(quit);
            menu.Opened += (_, _) => Refresh();

            var iconStream = Application.GetResourceStream(AppIcons.Uri)!.Stream;

            _icon = new TaskbarIcon
            {
                ToolTipText = _name,
                ContextMenu = menu,
                Icon = new System.Drawing.Icon(
                    iconStream,
                    (int)SystemParameters.SmallIconWidth,
                    (int)SystemParameters.SmallIconHeight),
                NoLeftClickDelay = true,
                LeftClickCommand = new ShowWindowCommand(window)
            };

            _icon.ForceCreate(enablesEfficiencyMode: false);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not create the tray icon; closing the window will quit.");

            _icon?.Dispose();
            _icon = null;

            return false;
        }

        notices.SeverityCounts.CollectionChanged += (_, _) => Refresh();
        window.HiddenToTray += OnHiddenToTray;
        window.HideOnClose = () => settings.Settings.Background.CloseToTray;

        Refresh();

        return true;
    }

    public void Dispose()
    {
        window.HideOnClose = null;
        window.HiddenToTray -= OnHiddenToTray;

        _icon?.Dispose();
        _icon = null;
    }


    private void Refresh()
    {
        var summary = notices.SeverityCounts.Count == 0
            ? "Nothing needs attention"
            : string.Join(", ", notices.SeverityCounts.Select(x => x.Label));

        _status?.Header = summary;
        _icon?.ToolTipText = $"{_name} - {summary}";
    }

    /// <summary>
    /// Says once, the first time the window disappears, that the app has not. Everybody who has used
    /// an app that quits when it is closed will otherwise assume it crashed, or - worse - launch it
    /// again and wonder why nothing happens.
    /// </summary>
    private void OnHiddenToTray(object? sender, EventArgs e)
    {
        if (settings.Settings.Background.TrayHintShown)
        {
            return;
        }

        settings.Settings.Background.TrayHintShown = true;
        settings.Save();

        _icon?.ShowNotification(
            $"{_name} is still running",
            "It keeps watching your mod folders from the tray. Right-click the icon to quit.",
            NotificationIcon.Info);
    }


    private sealed class ShowWindowCommand(MainWindow window) : ICommand
    {
        public event EventHandler? CanExecuteChanged { add { } remove { } }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter) => window.ShowFromTray();
    }
}
