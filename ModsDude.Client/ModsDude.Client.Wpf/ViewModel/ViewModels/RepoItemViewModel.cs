using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;
public class RepoItemViewModel(
    RepoModel repo,
    RepoPageViewModelFactory repoPageViewModelFactory)
    : IMenuItemViewModel
{
    public Guid Id => repo.Id;
    public string Title => repo.Name;

    public PageViewModel GetPage()
    {
        return repoPageViewModelFactory.Create(repo);
    }
}
