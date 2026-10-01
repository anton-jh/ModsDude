using System.Windows;

namespace ModsDude.Client.Wpf.Shared;

public interface IRemoteChangeWatcher : IDisposable
{
    void Start(Window window);
}
