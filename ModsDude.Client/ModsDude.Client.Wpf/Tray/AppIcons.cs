using ModsDude.Client.Core;

namespace ModsDude.Client.Wpf.Tray;

/// <summary>
/// Which icon this install wears. Blue is the real one and orange is anything else, so a debug build
/// beside the copy somebody is using is different in the tray and the taskbar, not only in a tooltip.
/// </summary>
public static class AppIcons
{
    public static Uri Uri { get; } = new(AppIdentity.IsProduction
        ? "pack://application:,,,/Assets/AppIcon.ico"
        : "pack://application:,,,/Assets/AppIcon.Dev.ico");
}
