using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class RepoItemViewModel(
    RepoModel repo,
    RepoPageViewModelFactory repoPageViewModelFactory)
    : MenuItemViewModel(
        repo.Name,
        () => repoPageViewModelFactory.Create(repo))
{
    public Guid Id => repo.Id;
}
