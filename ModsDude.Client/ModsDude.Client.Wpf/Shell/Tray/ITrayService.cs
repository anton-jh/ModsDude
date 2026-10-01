namespace ModsDude.Client.Wpf.Shell.Tray;

public interface ITrayService : IDisposable
{
    /// <returns>Whether the icon is up. False leaves the app behaving as if there were no tray.</returns>
    bool Start();
}
