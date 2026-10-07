using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.Connectivity;
using System.Windows;

namespace ModsDude.Client.Wpf.Shell.Offline;

/// <summary>
/// Covers the whole window while the server does not answer, because nothing that needs it works
/// until it does. Getting back is <see cref="ServerReconnect"/>'s; this only shows how it is going.
/// </summary>
public sealed partial class OfflineViewModel : ObservableObject, IDisposable
{
    private readonly IServerConnection _connection;
    private readonly IConnectionRetry _retry;
    private readonly TimeProvider _timeProvider;


    public OfflineViewModel(IServerConnection connection, IConnectionRetry retry, TimeProvider timeProvider)
    {
        _connection = connection;
        _retry = retry;
        _timeProvider = timeProvider;

        _connection.Changed += OnChanged;
        _retry.Changed += OnChanged;
    }


    public bool IsVisible => _connection.IsOnline is false;

    public string Attempts => _retry.GetOutage(ConnectionTarget.Server)?.Failures.ToString() ?? "-";

    public string NextAttempt => _retry.GetOutage(ConnectionTarget.Server)?.NextAttempt is DateTimeOffset at
        ? TimeZoneInfo.ConvertTime(at, _timeProvider.LocalTimeZone).ToString("HH:mm:ss")
        : "Now";


    [RelayCommand]
    private void RetryNow() => _retry.RetryNow();


    public void Dispose()
    {
        _connection.Changed -= OnChanged;
        _retry.Changed -= OnChanged;
    }


    private void OnChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.InvokeAsync(() => OnPropertyChanged(string.Empty));
    }
}
