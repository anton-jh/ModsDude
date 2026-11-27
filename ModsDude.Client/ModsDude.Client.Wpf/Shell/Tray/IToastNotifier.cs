using ModsDude.Client.Core.GameAdapters;

namespace ModsDude.Client.Wpf.Shell.Tray;

public interface IToastNotifier
{
    void Start();

    /// <summary>
    /// Keeps the digest quiet about these games while their start reminder is being prepared, so a
    /// notice the refresh for it finds is said once. Ended by <see cref="RemindOnStart"/> or
    /// <see cref="EndStart"/>. On the UI thread.
    /// </summary>
    void BeginStart(IReadOnlyList<GameIdentity> games);

    /// <summary>Rebuilds the notices and reminds about what is wrong with each of these games. On the UI thread.</summary>
    void RemindOnStart(IReadOnlyList<GameIdentity> games);

    /// <summary>Lets the digest speak about these games again without a reminder. On the UI thread.</summary>
    void EndStart(IReadOnlyList<GameIdentity> games);
}
