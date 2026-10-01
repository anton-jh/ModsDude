namespace ModsDude.Client.Wpf.Shell.Tray;

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

    /// <summary>
    /// Claims this process's identity for notifications. Before the first window, so the taskbar button
    /// and the shortcut agree on who this is.
    /// </summary>
    /// <returns>Whether toasts can be sent at all. False leaves every <see cref="Show"/> a no-op.</returns>
    bool Register(string appUserModelId, string displayName, string? executablePath, bool ensureShortcut = true);

    void Show(SystemToast toast);

    /// <summary>Takes back every toast this app has up, which are stale once somebody is looking at the window.</summary>
    void ClearAll();
}
