using ModsDude.Client.Core.Models;
using ModsDude.Client.Wpf.ViewModel.Pages;
using ModsDude.Client.Wpf.ViewModel.ViewModelFactories;

namespace ModsDude.Client.Wpf.ViewModel.ViewModels;

public class InstanceItemViewModel(
    RepoModel repo,
    LocalInstance instance,
    EditLocalInstancePageViewModelFactory pageFactory)
    : IMenuItemViewModel
{
    public string Title => instance.Name;

    public PageViewModel GetPage()
    {
        return pageFactory.Create(repo, instance);
    }
}
