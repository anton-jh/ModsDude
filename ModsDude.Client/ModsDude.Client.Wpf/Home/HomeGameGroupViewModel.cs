using CommunityToolkit.Mvvm.ComponentModel;
using ModsDude.Client.Core.GameAdapters;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Home;

/// <param name="IsConnected">Whether this machine has the game connected.</param>
public sealed record HomeGameState(string Name, bool IsConnected, bool IsRunning);


/// <summary>One game on Home, over the repos that are for it.</summary>
public sealed partial class HomeGameGroupViewModel(GameIdentity scope, HomeGameState state) : ObservableObject
{
    public GameIdentity Scope { get; } = scope;

    [ObservableProperty]
    private HomeGameState _state = state;

    public ObservableCollection<HomeRepoRowViewModel> Repos { get; } = [];
}
