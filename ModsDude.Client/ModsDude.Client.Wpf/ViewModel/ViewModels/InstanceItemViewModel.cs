using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class InstanceItemViewModel
    : MenuItemViewModel
{
    public InstanceItemViewModel(
        Repo repo,
        Game game,
        GamePageViewModel.Factory pageFactory)
        : base(
            game.Name,
            () => pageFactory.Create(repo, game),
            game,
            () => game.Name,
            nameof(Game.Name))
    {
        Icon = MenuIcons.Game;
    }
}
