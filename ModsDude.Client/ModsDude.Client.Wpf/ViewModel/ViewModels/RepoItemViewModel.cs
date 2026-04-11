using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class RepoItemViewModel(
    Repo repo,
    RepoPageViewModel.Factory repoPageViewModelFactory)
    : MenuItemViewModel(
        repo.Name,
        () => repoPageViewModelFactory.Create(repo))
{
    public Guid Id => repo.Id;
}
