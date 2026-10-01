using ModsDude.Client.Core.Models;
using ModsDude.Client.Core.Services;
using ModsDude.Client.Wpf.Shell.Toasts;
using System.Collections.Specialized;

namespace ModsDude.Client.Wpf.Games;

/// <summary>
/// Says when a game is connected or disconnected, whether somebody did it or it happened by itself
/// on finding the game installed.
/// </summary>
public sealed class GameConnectionToasts(GameRepository gameRepository, IToastService toasts)
    : IGameConnectionToasts
{
    public void Start()
    {
        gameRepository.Games.CollectionChanged += OnGamesChanged;
    }

    public void Dispose()
    {
        gameRepository.Games.CollectionChanged -= OnGamesChanged;
    }


    private void OnGamesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var game in e.NewItems?.OfType<Game>() ?? [])
        {
            toasts.Show($"Connected {game.Name}.");
        }

        foreach (var game in e.OldItems?.OfType<Game>() ?? [])
        {
            toasts.Show($"Disconnected {game.Name}.");
        }
    }
}
