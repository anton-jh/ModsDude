using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ModsDude.Client.Core.GameAdapters;
using System.Collections.ObjectModel;

namespace ModsDude.Client.Wpf.Home;

/// <param name="IsConnected">Whether this machine has the game connected.</param>
/// <param name="HasRepos">Whether any repo this account is in is about the game.</param>
public sealed record HomeGameState(string Name, bool IsConnected, bool IsRunning, bool HasRepos);


/// <summary>One game on Home, over the repos that are for it.</summary>
/// <param name="open">Opens the game's own page.</param>
public sealed partial class HomeGameGroupViewModel(GameIdentity scope, HomeGameState state, Action<GameIdentity> open) : ObservableObject
{
    public GameIdentity Scope { get; } = scope;

    [RelayCommand]
    private void Open() => open(Scope);

    [ObservableProperty]
    private HomeGameState _state = state;

    public ObservableCollection<HomeRepoRowViewModel> Repos { get; } = [];
}
