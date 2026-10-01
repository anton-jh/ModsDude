using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Builds;
using ModsDude.Client.Core.Connectivity;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Updates;

/// <summary>
/// Covers the whole window while the server refuses this build, because nothing that needs the server
/// works until it is resolved.
/// </summary>
public sealed partial class BuildMismatchViewModel : ObservableObject, IDisposable
{
    private readonly IServerCompatibility _compatibility;
    private readonly IAppUpdater _updater;
    private readonly IConnectionRetry _connection;


    public BuildMismatchViewModel(IServerCompatibility compatibility, IAppUpdater updater, IConnectionRetry connection)
    {
        _compatibility = compatibility;
        _updater = updater;
        _connection = connection;

        _compatibility.Changed += OnChanged;
        _updater.Changed += OnChanged;
    }


    private BuildMismatch? Mismatch => _compatibility.Mismatch;

    public bool IsVisible => Mismatch is not null;

    public bool IsClientBehind => Mismatch?.ClientIsBehind is true;

    public bool CanUpdate => IsClientBehind && _updater.IsInstalled;

    public bool HasActions => CanUpdate || IsClientBehind is false;

    public string Title => IsClientBehind ? "Update required" : "The server is out of date";

    public string ClientBuild => AppBuild.Description;

    public string ServerBuild => Mismatch?.Server.ToString() ?? "";

    public string UpdateStatus => _updater.StatusText;


    [RelayCommand(CanExecute = nameof(CanRestart))]
    private Task Restart() => _updater.RestartAsync();

    private bool CanRestart() => _updater.ReadyBuild is not null;

    [RelayCommand]
    private void Retry() => _connection.RetryNow();


    public void Dispose()
    {
        _compatibility.Changed -= OnChanged;
        _updater.Changed -= OnChanged;
    }


    private void OnChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            OnPropertyChanged(string.Empty);
            RestartCommand.NotifyCanExecuteChanged();
        });
    }
}
