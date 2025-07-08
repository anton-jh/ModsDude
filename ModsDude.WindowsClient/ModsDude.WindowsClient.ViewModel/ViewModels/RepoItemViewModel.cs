using ModsDude.WindowsClient.Model.Models;
using ModsDude.WindowsClient.ViewModel.Pages;
using ModsDude.WindowsClient.ViewModel.ViewModelFactories;

namespace ModsDude.WindowsClient.ViewModel.ViewModels;
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
